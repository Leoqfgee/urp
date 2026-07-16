#include <algorithm>
#include <chrono>
#include <cmath>
#include <cstdint>
#include <cstring>
#include <memory>
#include <mutex>
#include <unordered_map>
#include <vector>

#include <opencv2/calib3d.hpp>
#include <opencv2/core.hpp>
#include <opencv2/features2d.hpp>
#include <opencv2/imgproc.hpp>

namespace
{
constexpr char kModelMagic[8] = {'U', 'R', 'P', '3', 'D', 'M', '1', '\0'};
constexpr char kBuildVersion[] = "urp-orb-native-2026.07.16-r3-16k";
constexpr int kDescriptorBytes = 32;
constexpr int kModelRecordBytes = 3 * static_cast<int>(sizeof(float)) + kDescriptorBytes;

struct UrpOrbResult
{
    int tracked;
    int goodMatches;
    float centerX01;
    float centerY01;
    float relativeWidth;
    float topLeftX01;
    float topLeftY01;
    float topRightX01;
    float topRightY01;
    float bottomRightX01;
    float bottomRightY01;
    float bottomLeftX01;
    float bottomLeftY01;
    int poseValid;
    int poseInliers;
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
    float anchorX01;
    float anchorY01;
    float anchorDepth;
    int anchorVisible;
    float localLuminance;
    float inlierRatio;
    float coverageX;
    float coverageY;
    float modelSpread;
    float processingMilliseconds;
};

static float Clamp01(float value)
{
    return std::max(0.0f, std::min(1.0f, value));
}

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

    void SetRepairAnchor(float x, float y, float z)
    {
        repairAnchor_ = cv::Point3f(x, y, z);
        hasRepairAnchor_ = true;
    }

    int Track(const uint8_t* rgba, int width, int height, float fx, float fy, float cx, float cy, int rotationClockwise, UrpOrbResult* result)
    {
        const auto startedAt = std::chrono::steady_clock::now();
        if (result == nullptr)
        {
            return 0;
        }

        *result = UrpOrbResult{0};
        result->centerX01 = 0.5f;
        result->centerY01 = 0.5f;
        result->relativeWidth = 0.2f;
        result->topLeftX01 = 0.4f;
        result->topLeftY01 = 0.6f;
        result->topRightX01 = 0.6f;
        result->topRightY01 = 0.6f;
        result->bottomRightX01 = 0.6f;
        result->bottomRightY01 = 0.4f;
        result->bottomLeftX01 = 0.4f;
        result->bottomLeftY01 = 0.4f;
        result->r00 = 1.0f;
        result->r11 = 1.0f;
        result->r22 = 1.0f;
        result->reprojectionError = 999.0f;
        result->anchorX01 = 0.5f;
        result->anchorY01 = 0.5f;

        if (rgba == nullptr || width <= 0 || height <= 0 || targetDescriptors_.empty())
        {
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

        std::vector<cv::KeyPoint> frameKeypoints;
        cv::Mat frameDescriptors;
        orb_->detectAndCompute(gray, cv::noArray(), frameKeypoints, frameDescriptors);
        if (frameDescriptors.empty() || frameKeypoints.size() < 8)
        {
            return 0;
        }

        std::vector<std::vector<cv::DMatch>> targetToFrame;
        std::vector<std::vector<cv::DMatch>> frameToTarget;
        matcher_->knnMatch(targetDescriptors_, frameDescriptors, targetToFrame, 2);
        matcher_->knnMatch(frameDescriptors, targetDescriptors_, frameToTarget, 2);

        std::vector<int> reverseBest(frameDescriptors.rows, -1);
        for (const auto& pair : frameToTarget)
        {
            if (pair.size() >= 2 && pair[0].distance < ratio_ * pair[1].distance)
            {
                reverseBest[pair[0].queryIdx] = pair[0].trainIdx;
            }
        }

        std::vector<cv::DMatch> mutualMatches;
        mutualMatches.reserve(targetToFrame.size());
        for (const auto& pair : targetToFrame)
        {
            if (pair.size() >= 2
                && pair[0].distance < ratio_ * pair[1].distance
                && pair[0].trainIdx >= 0
                && pair[0].trainIdx < static_cast<int>(reverseBest.size())
                && reverseBest[pair[0].trainIdx] == pair[0].queryIdx)
            {
                mutualMatches.push_back(pair[0]);
            }
        }

        std::sort(mutualMatches.begin(), mutualMatches.end(), [](const cv::DMatch& a, const cv::DMatch& b)
        {
            return a.distance < b.distance;
        });
        const int gridColumns = 8;
        const int gridRows = 12;
        const int maxMatchesPerCell = 8;
        std::vector<int> cellCounts(gridColumns * gridRows, 0);
        std::vector<cv::DMatch> goodMatches;
        goodMatches.reserve(mutualMatches.size());
        for (const cv::DMatch& match : mutualMatches)
        {
            const cv::Point2f point = frameKeypoints[match.trainIdx].pt;
            const int column = std::clamp(
                static_cast<int>(point.x / std::max(1.0f, static_cast<float>(frame.cols)) * gridColumns),
                0,
                gridColumns - 1);
            const int row = std::clamp(
                static_cast<int>(point.y / std::max(1.0f, static_cast<float>(frame.rows)) * gridRows),
                0,
                gridRows - 1);
            const int cell = row * gridColumns + column;
            if (cellCounts[cell] < maxMatchesPerCell)
            {
                cellCounts[cell]++;
                goodMatches.push_back(match);
            }
        }

        result->goodMatches = static_cast<int>(goodMatches.size());
        if (static_cast<int>(goodMatches.size()) < minMatches_)
        {
            return 0;
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

        FillMatchedPointBox(framePoints, frame.cols, frame.rows, result);
        cv::Rect2f matchedBounds = cv::boundingRect(framePoints);
        result->coverageX = matchedBounds.width / std::max(1.0f, static_cast<float>(frame.cols));
        result->coverageY = matchedBounds.height / std::max(1.0f, static_cast<float>(frame.rows));
        cv::Point3f minimum = modelPoints.front();
        cv::Point3f maximum = modelPoints.front();
        for (const cv::Point3f& point : modelPoints)
        {
            minimum.x = std::min(minimum.x, point.x);
            minimum.y = std::min(minimum.y, point.y);
            minimum.z = std::min(minimum.z, point.z);
            maximum.x = std::max(maximum.x, point.x);
            maximum.y = std::max(maximum.y, point.y);
            maximum.z = std::max(maximum.z, point.z);
        }
        result->modelSpread = std::min({
            maximum.x - minimum.x,
            maximum.y - minimum.y,
            maximum.z - minimum.z});
        cv::Rect luminanceRegion = cv::boundingRect(framePoints);
        luminanceRegion &= cv::Rect(0, 0, gray.cols, gray.rows);
        if (luminanceRegion.width > 4 && luminanceRegion.height > 4)
        {
            result->localLuminance = static_cast<float>(cv::mean(gray(luminanceRegion))[0] / 255.0);
        }

        double scaledFx = orientedFx > 1.0 ? orientedFx * resizeScale : static_cast<double>(frame.cols) * 0.9;
        double scaledFy = orientedFy > 1.0 ? orientedFy * resizeScale : static_cast<double>(frame.cols) * 0.9;
        double scaledCx = orientedCx > 1.0 ? orientedCx * resizeScale : static_cast<double>(frame.cols) * 0.5;
        double scaledCy = orientedCy > 1.0 ? orientedCy * resizeScale : static_cast<double>(frame.rows) * 0.5;
        cv::Mat cameraMatrix = (cv::Mat_<double>(3, 3) << scaledFx, 0.0, scaledCx, 0.0, scaledFy, scaledCy, 0.0, 0.0, 1.0);
        cv::Mat distCoeffs = cv::Mat::zeros(4, 1, CV_64F);

        cv::Mat rvec;
        cv::Mat tvec;
        cv::Mat inliers;
        bool poseOk = false;
        if (modelPoints.size() >= 6)
        {
            poseOk = cv::solvePnPRansac(
                modelPoints,
                framePoints,
                cameraMatrix,
                distCoeffs,
                rvec,
                tvec,
                false,
                200,
                3.0f,
                0.99,
                inliers,
                cv::SOLVEPNP_EPNP);
        }

        const float inlierRatio = goodMatches.empty()
            ? 0.0f
            : static_cast<float>(inliers.rows) / static_cast<float>(goodMatches.size());
        result->inlierRatio = inlierRatio;
        if (poseOk
            && tvec.at<double>(2) > 0.0
            && inliers.rows >= std::max(20, minMatches_ / 2)
            && inlierRatio >= 0.5f
            && result->coverageX >= 0.12f
            && result->coverageY >= 0.20f
            && result->modelSpread >= 0.015f)
        {
            std::vector<cv::Point3f> inlierModelPoints;
            std::vector<cv::Point2f> inlierFramePoints;
            inlierModelPoints.reserve(inliers.rows);
            inlierFramePoints.reserve(inliers.rows);
            for (int row = 0; row < inliers.rows; row++)
            {
                int index = inliers.at<int>(row);
                inlierModelPoints.push_back(modelPoints[index]);
                inlierFramePoints.push_back(framePoints[index]);
            }
            cv::solvePnPRefineLM(inlierModelPoints, inlierFramePoints, cameraMatrix, distCoeffs, rvec, tvec);
            std::vector<cv::Point2f> projectedInliers;
            cv::projectPoints(inlierModelPoints, rvec, tvec, cameraMatrix, distCoeffs, projectedInliers);
            double squaredError = 0.0;
            for (size_t i = 0; i < projectedInliers.size(); i++)
            {
                cv::Point2f delta = projectedInliers[i] - inlierFramePoints[i];
                squaredError += static_cast<double>(delta.dot(delta));
            }

            result->reprojectionError = projectedInliers.empty()
                ? 999.0f
                : static_cast<float>(std::sqrt(squaredError / projectedInliers.size()));

            cv::Mat rotation;
            cv::Rodrigues(rvec, rotation);
            result->tracked = 1;
            result->poseValid = result->reprojectionError <= 2.5f ? 1 : 0;
            result->poseInliers = inliers.rows;
            result->tvecX = static_cast<float>(tvec.at<double>(0));
            result->tvecY = static_cast<float>(tvec.at<double>(1));
            result->tvecZ = static_cast<float>(tvec.at<double>(2));
            result->r00 = static_cast<float>(rotation.at<double>(0, 0));
            result->r01 = static_cast<float>(rotation.at<double>(0, 1));
            result->r02 = static_cast<float>(rotation.at<double>(0, 2));
            result->r10 = static_cast<float>(rotation.at<double>(1, 0));
            result->r11 = static_cast<float>(rotation.at<double>(1, 1));
            result->r12 = static_cast<float>(rotation.at<double>(1, 2));
            result->r20 = static_cast<float>(rotation.at<double>(2, 0));
            result->r21 = static_cast<float>(rotation.at<double>(2, 1));
            result->r22 = static_cast<float>(rotation.at<double>(2, 2));

            if (hasRepairAnchor_)
            {
                std::vector<cv::Point3f> anchorPoints{repairAnchor_};
                std::vector<cv::Point2f> projectedAnchor;
                cv::projectPoints(anchorPoints, rvec, tvec, cameraMatrix, distCoeffs, projectedAnchor);
                cv::Mat anchorVector = (cv::Mat_<double>(3, 1)
                    << repairAnchor_.x, repairAnchor_.y, repairAnchor_.z);
                cv::Mat anchorInCamera = rotation * anchorVector + tvec;
                float anchorX = projectedAnchor[0].x / static_cast<float>(frame.cols);
                float anchorY = 1.0f - projectedAnchor[0].y / static_cast<float>(frame.rows);
                result->anchorX01 = anchorX;
                result->anchorY01 = anchorY;
                result->anchorDepth = static_cast<float>(anchorInCamera.at<double>(2));
                result->anchorVisible = anchorX >= -0.05f && anchorX <= 1.05f
                    && anchorY >= -0.05f && anchorY <= 1.05f
                    && result->anchorDepth > 0.0f;
            }
        }

        result->processingMilliseconds = static_cast<float>(
            std::chrono::duration<double, std::milli>(
                std::chrono::steady_clock::now() - startedAt).count());
        return result->tracked;
    }

private:
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

    static void FillMatchedPointBox(const std::vector<cv::Point2f>& points, int frameWidth, int frameHeight, UrpOrbResult* result)
    {
        if (points.empty())
        {
            return;
        }

        float minX = points[0].x;
        float maxX = points[0].x;
        float minY = points[0].y;
        float maxY = points[0].y;
        for (const cv::Point2f& point : points)
        {
            minX = std::min(minX, point.x);
            maxX = std::max(maxX, point.x);
            minY = std::min(minY, point.y);
            maxY = std::max(maxY, point.y);
        }
        float boxWidth = std::max(1.0f, maxX - minX);
        float boxHeight = std::max(1.0f, maxY - minY);
        result->centerX01 = Clamp01((minX + boxWidth * 0.5f) / static_cast<float>(frameWidth));
        result->centerY01 = Clamp01(1.0f - ((minY + boxHeight * 0.5f) / static_cast<float>(frameHeight)));
        result->relativeWidth = Clamp01(boxWidth / static_cast<float>(frameWidth));
        result->topLeftX01 = Clamp01(minX / static_cast<float>(frameWidth));
        result->topLeftY01 = Clamp01(1.0f - minY / static_cast<float>(frameHeight));
        result->topRightX01 = Clamp01(maxX / static_cast<float>(frameWidth));
        result->topRightY01 = result->topLeftY01;
        result->bottomRightX01 = result->topRightX01;
        result->bottomRightY01 = Clamp01(1.0f - maxY / static_cast<float>(frameHeight));
        result->bottomLeftX01 = result->topLeftX01;
        result->bottomLeftY01 = result->bottomRightY01;
    }

    float ratio_;
    int minMatches_;
    int maxWidth_;
    cv::Ptr<cv::ORB> orb_;
    cv::Ptr<cv::BFMatcher> matcher_;
    std::vector<cv::Point3f> targetModelPoints_;
    cv::Mat targetDescriptors_;
    cv::Point3f repairAnchor_{0.0f, 0.0f, 0.0f};
    bool hasRepairAnchor_ = false;
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

    int urp_orb_set_repair_anchor(int handle, float x, float y, float z)
    {
        std::lock_guard<std::mutex> lock(gMutex);
        auto found = gTrackers.find(handle);
        if (found == gTrackers.end())
        {
            return 0;
        }

        found->second->SetRepairAnchor(x, y, z);
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
