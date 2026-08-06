#include <algorithm>
#include <chrono>
#include <cmath>
#include <cstdint>
#include <cstring>
#include <memory>
#include <mutex>
#include <unordered_map>
#include <unordered_set>
#include <vector>

#include <opencv2/calib3d.hpp>
#include <opencv2/core.hpp>
#include <opencv2/features2d.hpp>
#include <opencv2/imgproc.hpp>

namespace
{
constexpr char kModelMagic[8] = {'U', 'R', 'P', '3', 'D', 'M', '1', '\0'};
constexpr char kBuildVersion[] = "urp-orb-native-2026.07.24-r8-multiview-pose-hsv";
constexpr int kDescriptorBytes = 32;
constexpr int kModelRecordBytes = 3 * static_cast<int>(sizeof(float)) + kDescriptorBytes;

struct UrpOrbResult
{
    int tracked;
    int poseValid;
    int poseInliers;
    int uniqueMatches;
    int detectedKeypoints;
    int ratioMatches;
    int guidedMatches;
    int occupiedGridCells;
    int rejectionCode;
    float tvecX;
    float tvecY;
    float tvecZ;
    float r00;
    float r01;
    float r02;
    float r10;
    float r11;
    float r12;
    float r20;
    float r21;
    float r22;
    float reprojectionError;
    float reprojectionMax;
    float inlierRatio;
    float coverageX;
    float coverageY;
    float processingMilliseconds;
    float sampledHue;
    float sampledSaturation;
    float sampledValue;
    float sampledConfidence;
};

enum RejectionCode
{
    kAccepted = 0,
    kInvalidInput = 1,
    kNoDescriptors = 2,
    kInsufficientUniqueMatches = 3,
    kInsufficientSpatialDistribution = 4,
    kPnpFailed = 5,
    kInsufficientPoseInliers = 6,
    kLowInlierRatio = 7,
    kHighReprojectionError = 8,
    kNegativeDepth = 9,
    kLowCountPoseUnstable = 10
};

class OrbTracker
{
public:
    OrbTracker(int featureCount, float ratio, int minMatches, int maxWidth)
        : ratio_(ratio), minMatches_(minMatches), maxWidth_(maxWidth)
    {
        orb_ = cv::ORB::create(std::max(100, featureCount));
        matcher_ = cv::BFMatcher::create(cv::NORM_HAMMING, false);
    }

    int SetModel(const uint8_t* data, int length)
    {
        if (data == nullptr || length < 12 || std::memcmp(data, kModelMagic, sizeof(kModelMagic)) != 0)
        {
            return 0;
        }

        uint32_t count = 0;
        std::memcpy(&count, data + 8, sizeof(count));
        size_t expectedLength = 12u + static_cast<size_t>(count) * static_cast<size_t>(kModelRecordBytes);
        if (count < 8 || expectedLength != static_cast<size_t>(length))
        {
            return 0;
        }

        targetModelPoints_.clear();
        targetModelPoints_.reserve(count);
        targetDescriptors_.create(static_cast<int>(count), kDescriptorBytes, CV_8UC1);
        const uint8_t* cursor = data + 12;
        for (uint32_t row = 0; row < count; row++)
        {
            float coordinates[3];
            std::memcpy(coordinates, cursor, sizeof(coordinates));
            cursor += sizeof(coordinates);
            targetModelPoints_.emplace_back(coordinates[0], coordinates[1], coordinates[2]);
            std::memcpy(targetDescriptors_.ptr(static_cast<int>(row)), cursor, kDescriptorBytes);
            cursor += kDescriptorBytes;
        }

        return targetDescriptors_.empty() || targetModelPoints_.size() < 8 ? 0 : 1;
    }

    int SetPosePrior(const float* rotationTranslation, float searchRadiusFraction)
    {
        if (rotationTranslation == nullptr)
        {
            hasPosePrior_ = false;
            return 0;
        }

        cv::Mat rotation(3, 3, CV_64F);
        cv::Mat translation(3, 1, CV_64F);
        for (int row = 0; row < 3; row++)
        {
            for (int column = 0; column < 3; column++)
            {
                const float value = rotationTranslation[row * 4 + column];
                if (!std::isfinite(value))
                {
                    hasPosePrior_ = false;
                    return 0;
                }
                rotation.at<double>(row, column) = value;
            }
            const float value = rotationTranslation[row * 4 + 3];
            if (!std::isfinite(value))
            {
                hasPosePrior_ = false;
                return 0;
            }
            translation.at<double>(row) = value;
        }

        const double determinant = cv::determinant(rotation);
        if (std::abs(determinant - 1.0) > 0.25
            || translation.at<double>(2) <= 0.0)
        {
            hasPosePrior_ = false;
            return 0;
        }

        priorRotation_ = rotation;
        priorTranslation_ = translation;
        priorSearchRadiusFraction_ =
            std::clamp(searchRadiusFraction, 0.08f, 0.35f);
        hasPosePrior_ = true;
        return 1;
    }

    void ClearPosePrior()
    {
        hasPosePrior_ = false;
    }

    int Track(const uint8_t* rgba, int width, int height, float fx, float fy, float cx, float cy, int rotationClockwise, UrpOrbResult* result)
    {
        const auto startedAt = std::chrono::steady_clock::now();
        if (result == nullptr)
        {
            return 0;
        }

        *result = UrpOrbResult{0};
        result->r00 = 1.0f;
        result->r11 = 1.0f;
        result->r22 = 1.0f;
        result->reprojectionError = 999.0f;
        result->reprojectionMax = 999.0f;

        if (rgba == nullptr || width <= 0 || height <= 0 || targetDescriptors_.empty())
        {
            result->rejectionCode = kInvalidInput;
            return 0;
        }

        cv::Mat source(height, width, CV_8UC4, const_cast<uint8_t*>(rgba));
        cv::Mat oriented;
        double orientedFx = fx;
        double orientedFy = fy;
        double orientedCx = cx;
        double orientedCy = cy;
        if (rotationClockwise == 90)
        {
            cv::rotate(source, oriented, cv::ROTATE_90_CLOCKWISE);
            orientedFx = fy;
            orientedFy = fx;
            orientedCx = static_cast<double>(height - 1) - cy;
            orientedCy = cx;
        }
        else if (rotationClockwise == 180)
        {
            cv::rotate(source, oriented, cv::ROTATE_180);
            orientedCx = static_cast<double>(width - 1) - cx;
            orientedCy = static_cast<double>(height - 1) - cy;
        }
        else if (rotationClockwise == 270)
        {
            cv::rotate(source, oriented, cv::ROTATE_90_COUNTERCLOCKWISE);
            orientedFx = fy;
            orientedFy = fx;
            orientedCx = cy;
            orientedCy = static_cast<double>(width - 1) - cx;
        }
        else
        {
            oriented = source;
        }

        double resizeScale = 1.0;
        cv::Mat frame = ResizeForTracking(oriented, resizeScale);
        cv::Mat gray;
        cv::cvtColor(frame, gray, cv::COLOR_RGBA2GRAY);

        double scaledFx = orientedFx > 1.0 ? orientedFx * resizeScale : static_cast<double>(frame.cols) * 0.9;
        double scaledFy = orientedFy > 1.0 ? orientedFy * resizeScale : static_cast<double>(frame.cols) * 0.9;
        double scaledCx = orientedCx > 1.0 ? orientedCx * resizeScale : static_cast<double>(frame.cols) * 0.5;
        double scaledCy = orientedCy > 1.0 ? orientedCy * resizeScale : static_cast<double>(frame.rows) * 0.5;
        cv::Mat cameraMatrix = (cv::Mat_<double>(3, 3) << scaledFx, 0.0, scaledCx, 0.0, scaledFy, scaledCy, 0.0, 0.0, 1.0);
        cv::Mat distCoeffs = cv::Mat::zeros(4, 1, CV_64F);

        std::vector<cv::KeyPoint> frameKeypoints;
        cv::Mat frameDescriptors;
        orb_->detectAndCompute(gray, cv::noArray(), frameKeypoints, frameDescriptors);
        if (frameDescriptors.empty() || frameKeypoints.size() < 8)
        {
            result->detectedKeypoints = static_cast<int>(frameKeypoints.size());
            result->rejectionCode = kNoDescriptors;
            return 0;
        }
        result->detectedKeypoints = static_cast<int>(frameKeypoints.size());

        cv::Mat orientedPriorRotation;
        cv::Mat orientedPriorTranslation;
        cv::Mat orientedPriorRvec;
        std::vector<cv::Point2f> projectedPrior;
        std::vector<float> projectedPriorDepth;
        bool hasUsablePrior = false;
        if (hasPosePrior_)
        {
            cv::Mat orientation = cv::Mat::eye(3, 3, CV_64F);
            if (rotationClockwise == 90)
            {
                orientation = (cv::Mat_<double>(3, 3)
                    << 0.0, -1.0, 0.0, 1.0, 0.0, 0.0, 0.0, 0.0, 1.0);
            }
            else if (rotationClockwise == 180)
            {
                orientation = (cv::Mat_<double>(3, 3)
                    << -1.0, 0.0, 0.0, 0.0, -1.0, 0.0, 0.0, 0.0, 1.0);
            }
            else if (rotationClockwise == 270)
            {
                orientation = (cv::Mat_<double>(3, 3)
                    << 0.0, 1.0, 0.0, -1.0, 0.0, 0.0, 0.0, 0.0, 1.0);
            }

            orientedPriorRotation = orientation * priorRotation_;
            orientedPriorTranslation = orientation * priorTranslation_;
            cv::Rodrigues(orientedPriorRotation, orientedPriorRvec);
            cv::projectPoints(
                targetModelPoints_,
                orientedPriorRvec,
                orientedPriorTranslation,
                cameraMatrix,
                distCoeffs,
                projectedPrior);
            projectedPriorDepth.resize(targetModelPoints_.size());
            for (size_t index = 0; index < targetModelPoints_.size(); index++)
            {
                const cv::Point3f& point = targetModelPoints_[index];
                projectedPriorDepth[index] = static_cast<float>(
                    orientedPriorRotation.at<double>(2, 0) * point.x
                    + orientedPriorRotation.at<double>(2, 1) * point.y
                    + orientedPriorRotation.at<double>(2, 2) * point.z
                    + orientedPriorTranslation.at<double>(2));
            }
            hasUsablePrior = projectedPrior.size() == targetModelPoints_.size();
        }

        std::vector<std::vector<cv::DMatch>> targetToFrame;
        matcher_->knnMatch(targetDescriptors_, frameDescriptors, targetToFrame, 2);
        std::vector<cv::DMatch> strictRatioMatches;
        std::vector<cv::DMatch> guidedMatches;
        strictRatioMatches.reserve(targetToFrame.size());
        guidedMatches.reserve(targetToFrame.size());
        const float guidedRatio = std::max(ratio_, 0.86f);
        const float guidedRadius = std::max(
            36.0f,
            priorSearchRadiusFraction_
                * static_cast<float>(std::min(frame.cols, frame.rows)));
        const float guidedRadiusSquared = guidedRadius * guidedRadius;
        for (const auto& pair : targetToFrame)
        {
            if (pair.size() < 2)
            {
                continue;
            }
            const cv::DMatch& match = pair[0];
            if (match.distance < ratio_ * pair[1].distance)
            {
                strictRatioMatches.push_back(match);
            }
            if (!hasUsablePrior
                || match.queryIdx < 0
                || match.queryIdx >= static_cast<int>(projectedPrior.size())
                || match.trainIdx < 0
                || match.trainIdx >= static_cast<int>(frameKeypoints.size())
                || projectedPriorDepth[match.queryIdx] <= 0.0f
                || match.distance > 64.0f
                || match.distance >= guidedRatio * pair[1].distance)
            {
                continue;
            }

            const cv::Point2f delta =
                frameKeypoints[match.trainIdx].pt - projectedPrior[match.queryIdx];
            if (delta.dot(delta) <= guidedRadiusSquared)
            {
                guidedMatches.push_back(match);
            }
        }

        result->ratioMatches = static_cast<int>(strictRatioMatches.size());
        result->guidedMatches = static_cast<int>(guidedMatches.size());
        const PoseSolution strictSolution = SolveCandidateSet(
            strictRatioMatches,
            frameKeypoints,
            frame.cols,
            frame.rows,
            cameraMatrix,
            distCoeffs,
            false,
            cv::Mat(),
            cv::Mat());
        PoseSolution guidedSolution;
        if (hasUsablePrior
            && static_cast<int>(guidedMatches.size()) >= minMatches_)
        {
            guidedSolution = SolveCandidateSet(
                guidedMatches,
                frameKeypoints,
                frame.cols,
                frame.rows,
                cameraMatrix,
                distCoeffs,
                true,
                orientedPriorRvec,
                orientedPriorTranslation);
        }

        PoseSolution chosen = strictSolution;
        if (IsBetterPose(guidedSolution, chosen))
        {
            chosen = guidedSolution;
        }
        result->uniqueMatches = chosen.uniqueMatches;
        result->occupiedGridCells = chosen.occupiedGridCells;
        result->coverageX = chosen.coverageX;
        result->coverageY = chosen.coverageY;
        result->poseInliers = chosen.poseInliers;
        result->inlierRatio = chosen.inlierRatio;
        result->reprojectionError = chosen.reprojectionError;
        result->reprojectionMax = chosen.reprojectionMax;
        if (!chosen.solved)
        {
            result->rejectionCode =
                chosen.uniqueMatches < minMatches_
                ? kInsufficientUniqueMatches
                : kPnpFailed;
        }
        else
        {
            result->tracked = 1;
            result->tvecX = static_cast<float>(chosen.tvec.at<double>(0));
            result->tvecY = static_cast<float>(chosen.tvec.at<double>(1));
            result->tvecZ = static_cast<float>(chosen.tvec.at<double>(2));
            result->r00 = static_cast<float>(chosen.rotation.at<double>(0, 0));
            result->r01 = static_cast<float>(chosen.rotation.at<double>(0, 1));
            result->r02 = static_cast<float>(chosen.rotation.at<double>(0, 2));
            result->r10 = static_cast<float>(chosen.rotation.at<double>(1, 0));
            result->r11 = static_cast<float>(chosen.rotation.at<double>(1, 1));
            result->r12 = static_cast<float>(chosen.rotation.at<double>(1, 2));
            result->r20 = static_cast<float>(chosen.rotation.at<double>(2, 0));
            result->r21 = static_cast<float>(chosen.rotation.at<double>(2, 1));
            result->r22 = static_cast<float>(chosen.rotation.at<double>(2, 2));

            const bool locallyGuided = chosen.usedPosePrior && hasUsablePrior;
            const float requiredInlierRatio = locallyGuided ? 0.34f : 0.40f;
            const int requiredPoseInliers = std::clamp(
                static_cast<int>(std::ceil(
                    chosen.uniqueMatches * requiredInlierRatio)),
                6,
                10);
            if (chosen.tvec.at<double>(2) <= 0.0)
                result->rejectionCode = kNegativeDepth;
            else if (chosen.poseInliers < requiredPoseInliers)
                result->rejectionCode = kInsufficientPoseInliers;
            else if (chosen.inlierRatio < requiredInlierRatio)
                result->rejectionCode = kLowInlierRatio;
            else if (chosen.reprojectionError > 3.0f
                || chosen.reprojectionMax > 8.0f)
                result->rejectionCode = kHighReprojectionError;
            else if (chosen.coverageX < (locallyGuided ? 0.035f : 0.05f)
                || chosen.coverageY < (locallyGuided ? 0.10f : 0.16f)
                || chosen.occupiedGridCells < (locallyGuided ? 3 : 4))
                result->rejectionCode = kInsufficientSpatialDistribution;
            else if (chosen.poseInliers < 8
                && (chosen.reprojectionError > 1.75f
                    || chosen.occupiedGridCells < 5))
                result->rejectionCode = kLowCountPoseUnstable;
            else
                result->rejectionCode = kAccepted;
            result->poseValid = result->rejectionCode == kAccepted ? 1 : 0;
            if (result->poseValid != 0)
            {
                SampleReferenceHsv(frame, chosen.inlierFramePoints, result);
            }
        }

        result->processingMilliseconds = static_cast<float>(
            std::chrono::duration<double, std::milli>(
                std::chrono::steady_clock::now() - startedAt).count());
        return result->tracked;
    }

private:
    struct PoseSolution
    {
        bool solved = false;
        bool usedPosePrior = false;
        int uniqueMatches = 0;
        int occupiedGridCells = 0;
        int poseInliers = 0;
        float coverageX = 0.0f;
        float coverageY = 0.0f;
        float inlierRatio = 0.0f;
        float reprojectionError = 999.0f;
        float reprojectionMax = 999.0f;
        cv::Mat rvec;
        cv::Mat tvec;
        cv::Mat rotation;
        std::vector<cv::Point2f> inlierFramePoints;
    };

    PoseSolution SolveCandidateSet(
        std::vector<cv::DMatch> candidateMatches,
        const std::vector<cv::KeyPoint>& frameKeypoints,
        int frameWidth,
        int frameHeight,
        const cv::Mat& cameraMatrix,
        const cv::Mat& distCoeffs,
        bool usePosePrior,
        const cv::Mat& priorRvec,
        const cv::Mat& priorTranslation) const
    {
        PoseSolution solution;
        solution.usedPosePrior = usePosePrior;
        std::sort(
            candidateMatches.begin(),
            candidateMatches.end(),
            [](const cv::DMatch& a, const cv::DMatch& b)
            {
                return a.distance < b.distance;
            });

        constexpr int gridColumns = 8;
        constexpr int gridRows = 12;
        constexpr int maxMatchesPerCell = 8;
        std::vector<int> cellCounts(gridColumns * gridRows, 0);
        std::vector<cv::DMatch> goodMatches;
        goodMatches.reserve(candidateMatches.size());
        std::unordered_set<int> usedModelRecords;
        std::unordered_set<int> usedFrameKeypoints;
        for (const cv::DMatch& match : candidateMatches)
        {
            if (match.queryIdx < 0
                || match.queryIdx >= static_cast<int>(targetModelPoints_.size())
                || match.trainIdx < 0
                || match.trainIdx >= static_cast<int>(frameKeypoints.size())
                || usedModelRecords.count(match.queryIdx) != 0
                || usedFrameKeypoints.count(match.trainIdx) != 0)
            {
                continue;
            }
            const cv::Point2f point = frameKeypoints[match.trainIdx].pt;
            const int column = std::clamp(
                static_cast<int>(
                    point.x / std::max(1.0f, static_cast<float>(frameWidth))
                    * gridColumns),
                0,
                gridColumns - 1);
            const int row = std::clamp(
                static_cast<int>(
                    point.y / std::max(1.0f, static_cast<float>(frameHeight))
                    * gridRows),
                0,
                gridRows - 1);
            const int cell = row * gridColumns + column;
            if (cellCounts[cell] >= maxMatchesPerCell)
            {
                continue;
            }
            cellCounts[cell]++;
            goodMatches.push_back(match);
            usedModelRecords.insert(match.queryIdx);
            usedFrameKeypoints.insert(match.trainIdx);
        }

        solution.uniqueMatches = static_cast<int>(goodMatches.size());
        solution.occupiedGridCells = static_cast<int>(std::count_if(
            cellCounts.begin(),
            cellCounts.end(),
            [](int count) { return count > 0; }));
        if (static_cast<int>(goodMatches.size()) < minMatches_)
        {
            return solution;
        }

        std::vector<cv::Point2f> framePoints;
        std::vector<cv::Point3f> modelPoints;
        framePoints.reserve(goodMatches.size());
        modelPoints.reserve(goodMatches.size());
        for (const cv::DMatch& match : goodMatches)
        {
            framePoints.push_back(frameKeypoints[match.trainIdx].pt);
            modelPoints.push_back(targetModelPoints_[match.queryIdx]);
        }
        const cv::Rect2f matchedBounds = cv::boundingRect(framePoints);
        const float inferredWidth =
            std::max(1.0f, static_cast<float>(frameWidth));
        const float inferredHeight =
            std::max(1.0f, static_cast<float>(frameHeight));
        solution.coverageX = matchedBounds.width / inferredWidth;
        solution.coverageY = matchedBounds.height / inferredHeight;

        struct Attempt
        {
            bool solved = false;
            cv::Mat rvec;
            cv::Mat tvec;
            cv::Mat inliers;
            int inlierCount = 0;
            float inlierRatio = 0.0f;
            float rms = 999.0f;
            float maximumError = 999.0f;
        };
        Attempt best;
        auto trySolver = [&](int flag, bool useGuess)
        {
            Attempt attempt;
            if (useGuess)
            {
                attempt.rvec = priorRvec.clone();
                attempt.tvec = priorTranslation.clone();
            }
            try
            {
                attempt.solved = cv::solvePnPRansac(
                    modelPoints,
                    framePoints,
                    cameraMatrix,
                    distCoeffs,
                    attempt.rvec,
                    attempt.tvec,
                    useGuess,
                    400,
                    4.0f,
                    0.995,
                    attempt.inliers,
                    flag);
            }
            catch (const cv::Exception&)
            {
                attempt.solved = false;
            }
            if (!attempt.solved || attempt.inliers.rows < 4)
            {
                return;
            }

            std::vector<cv::Point3f> inlierModelPoints;
            std::vector<cv::Point2f> inlierFramePoints;
            inlierModelPoints.reserve(attempt.inliers.rows);
            inlierFramePoints.reserve(attempt.inliers.rows);
            for (int row = 0; row < attempt.inliers.rows; row++)
            {
                const int index = attempt.inliers.at<int>(row);
                inlierModelPoints.push_back(modelPoints[index]);
                inlierFramePoints.push_back(framePoints[index]);
            }
            try
            {
                cv::solvePnPRefineLM(
                    inlierModelPoints,
                    inlierFramePoints,
                    cameraMatrix,
                    distCoeffs,
                    attempt.rvec,
                    attempt.tvec);
            }
            catch (const cv::Exception&)
            {
                return;
            }

            std::vector<cv::Point2f> projected;
            cv::projectPoints(
                inlierModelPoints,
                attempt.rvec,
                attempt.tvec,
                cameraMatrix,
                distCoeffs,
                projected);
            double squaredError = 0.0;
            double maximumError = 0.0;
            for (size_t index = 0; index < projected.size(); index++)
            {
                const cv::Point2f delta = projected[index] - inlierFramePoints[index];
                const double errorSquared = static_cast<double>(delta.dot(delta));
                squaredError += errorSquared;
                maximumError = std::max(maximumError, std::sqrt(errorSquared));
            }
            attempt.inlierCount = attempt.inliers.rows;
            attempt.inlierRatio =
                static_cast<float>(attempt.inlierCount)
                / static_cast<float>(std::max<size_t>(1, goodMatches.size()));
            attempt.rms = projected.empty()
                ? 999.0f
                : static_cast<float>(
                    std::sqrt(squaredError / projected.size()));
            attempt.maximumError = static_cast<float>(maximumError);
            const float score =
                attempt.inlierCount * 4.0f
                + attempt.inlierRatio * 12.0f
                - attempt.rms;
            const float bestScore =
                best.inlierCount * 4.0f
                + best.inlierRatio * 12.0f
                - best.rms;
            if (!best.solved || score > bestScore)
            {
                best = attempt;
            }
        };

        if (usePosePrior)
        {
            trySolver(cv::SOLVEPNP_ITERATIVE, true);
        }
        trySolver(cv::SOLVEPNP_SQPNP, false);
        trySolver(cv::SOLVEPNP_EPNP, false);
        trySolver(cv::SOLVEPNP_ITERATIVE, false);
        if (!best.solved)
        {
            return solution;
        }

        solution.solved = true;
        solution.poseInliers = best.inlierCount;
        solution.inlierRatio = best.inlierRatio;
        solution.reprojectionError = best.rms;
        solution.reprojectionMax = best.maximumError;
        solution.rvec = best.rvec;
        solution.tvec = best.tvec;
        cv::Rodrigues(solution.rvec, solution.rotation);
        solution.inlierFramePoints.reserve(best.inliers.rows);
        for (int row = 0; row < best.inliers.rows; row++)
        {
            const int index = best.inliers.at<int>(row);
            solution.inlierFramePoints.push_back(framePoints[index]);
        }
        return solution;
    }

    static bool IsBetterPose(
        const PoseSolution& candidate,
        const PoseSolution& current)
    {
        if (candidate.solved != current.solved)
        {
            return candidate.solved;
        }
        if (!candidate.solved)
        {
            return candidate.uniqueMatches > current.uniqueMatches;
        }
        const float candidateScore =
            candidate.poseInliers * 4.0f
            + candidate.inlierRatio * 12.0f
            - candidate.reprojectionError;
        const float currentScore =
            current.poseInliers * 4.0f
            + current.inlierRatio * 12.0f
            - current.reprojectionError;
        return candidateScore > currentScore;
    }

    static void SampleReferenceHsv(
        const cv::Mat& rgbaFrame,
        const std::vector<cv::Point2f>& sampleCenters,
        UrpOrbResult* result)
    {
        if (rgbaFrame.empty() || sampleCenters.empty() || result == nullptr)
        {
            return;
        }
        cv::Mat rgb;
        cv::Mat hsv;
        cv::cvtColor(rgbaFrame, rgb, cv::COLOR_RGBA2RGB);
        cv::cvtColor(rgb, hsv, cv::COLOR_RGB2HSV);
        double hueX = 0.0;
        double hueY = 0.0;
        double saturation = 0.0;
        double value = 0.0;
        int count = 0;
        constexpr int radius = 4;
        for (const cv::Point2f& center : sampleCenters)
        {
            const int centerX = cvRound(center.x);
            const int centerY = cvRound(center.y);
            for (int y = centerY - radius; y <= centerY + radius; y += 2)
            {
                for (int x = centerX - radius; x <= centerX + radius; x += 2)
                {
                    if (x < 0 || y < 0 || x >= hsv.cols || y >= hsv.rows)
                    {
                        continue;
                    }
                    const cv::Vec3b pixel = hsv.at<cv::Vec3b>(y, x);
                    if (pixel[1] > 135 || pixel[2] < 70 || pixel[2] > 250)
                    {
                        continue;
                    }
                    const double angle =
                        static_cast<double>(pixel[0]) / 180.0 * 2.0 * CV_PI;
                    const double hueWeight =
                        std::max(0.05, static_cast<double>(pixel[1]) / 255.0);
                    hueX += std::cos(angle) * hueWeight;
                    hueY += std::sin(angle) * hueWeight;
                    saturation += static_cast<double>(pixel[1]) / 255.0;
                    value += static_cast<double>(pixel[2]) / 255.0;
                    count++;
                }
            }
        }
        if (count < 12)
        {
            return;
        }
        double hueAngle = std::atan2(hueY, hueX);
        if (hueAngle < 0.0)
        {
            hueAngle += 2.0 * CV_PI;
        }
        result->sampledHue = static_cast<float>(hueAngle / (2.0 * CV_PI));
        result->sampledSaturation =
            static_cast<float>(saturation / static_cast<double>(count));
        result->sampledValue =
            static_cast<float>(value / static_cast<double>(count));
        result->sampledConfidence = std::clamp(
            static_cast<float>(count)
                / static_cast<float>(sampleCenters.size() * 25),
            0.0f,
            1.0f);
    }

    cv::Mat ResizeForTracking(const cv::Mat& source, double& scale) const
    {
        if (source.cols <= maxWidth_)
        {
            scale = 1.0;
            return source;
        }

        scale = static_cast<double>(maxWidth_) / static_cast<double>(source.cols);
        cv::Mat resized;
        cv::resize(source, resized, cv::Size(maxWidth_, static_cast<int>(source.rows * scale)));
        return resized;
    }

    float ratio_;
    int minMatches_;
    int maxWidth_;
    cv::Ptr<cv::ORB> orb_;
    cv::Ptr<cv::BFMatcher> matcher_;
    std::vector<cv::Point3f> targetModelPoints_;
    cv::Mat targetDescriptors_;
    cv::Mat priorRotation_ = cv::Mat::eye(3, 3, CV_64F);
    cv::Mat priorTranslation_ = cv::Mat::zeros(3, 1, CV_64F);
    float priorSearchRadiusFraction_ = 0.12f;
    bool hasPosePrior_ = false;
};

static std::mutex gMutex;
static int gNextHandle = 1;
static std::unordered_map<int, std::unique_ptr<OrbTracker>> gTrackers;
}

extern "C"
{
    const char* urp_orb_get_build_version()
    {
        return kBuildVersion;
    }

    int urp_orb_create(int featureCount, float ratio, int minMatches, int maxWidth)
    {
        std::lock_guard<std::mutex> lock(gMutex);
        int handle = gNextHandle++;
        gTrackers[handle] = std::make_unique<OrbTracker>(featureCount, ratio, minMatches, maxWidth);
        return handle;
    }

    void urp_orb_destroy(int handle)
    {
        std::lock_guard<std::mutex> lock(gMutex);
        gTrackers.erase(handle);
    }

    int urp_orb_set_model(int handle, const uint8_t* data, int length)
    {
        std::lock_guard<std::mutex> lock(gMutex);
        auto found = gTrackers.find(handle);
        if (found == gTrackers.end())
        {
            return 0;
        }

        return found->second->SetModel(data, length);
    }

    int urp_orb_set_pose_prior(
        int handle,
        const float* rotationTranslation,
        float searchRadiusFraction)
    {
        std::lock_guard<std::mutex> lock(gMutex);
        auto found = gTrackers.find(handle);
        if (found == gTrackers.end())
        {
            return 0;
        }
        return found->second->SetPosePrior(
            rotationTranslation,
            searchRadiusFraction);
    }

    int urp_orb_clear_pose_prior(int handle)
    {
        std::lock_guard<std::mutex> lock(gMutex);
        auto found = gTrackers.find(handle);
        if (found == gTrackers.end())
        {
            return 0;
        }
        found->second->ClearPosePrior();
        return 1;
    }

    int urp_orb_track(int handle, const uint8_t* rgba, int width, int height, float fx, float fy, float cx, float cy, int rotationClockwise, UrpOrbResult* result)
    {
        std::lock_guard<std::mutex> lock(gMutex);
        auto found = gTrackers.find(handle);
        if (found == gTrackers.end())
        {
            return 0;
        }

        return found->second->Track(rgba, width, height, fx, fy, cx, cy, rotationClockwise, result);
    }
}
