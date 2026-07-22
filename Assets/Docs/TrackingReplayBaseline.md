# Bottle ORB offline replay baseline

Run date: 2026-07-17

Command:

```powershell
python Tools/TrackingReplay/replay_orb.py `
  --model Assets/OrbModels/bottle_reference_b.bytes `
  --frames "F:\Au\暑期任务\抽帧照片\bottle_damaged" `
  --output Builds/tracking-replay-final `
  --step 12 --minimum-matches 9
```

The July 22 replay sampled 20 of the 240 open/no-cap bottle frames. Ten passed
every gate, eight stopped below nine unique reciprocal matches, and two reached
PnP but failed the 50% inlier-ratio gate. This is a source-photo baseline, not
a claim of phone-camera acceptance.

The important low-count regression cases are:

| Frame | Unique | Grid cells | Width / height coverage | PnP inliers | Ratio | RMS | Result |
|---|---:|---:|---:|---:|---:|---:|---|
| 0133 | 11 | 5 | 47.4% / 66.0% | 6 | 54.5% | 0.24 px | accepted by low-count precision gate |
| 0181 | 15 | 7 | 44.7% / 77.0% | 10 | 66.7% | 1.50 px | accepted |
| 0217 | 16 | 6 | 52.5% / 58.0% | 12 | 75.0% | 1.62 px | accepted |
| 0229 | 18 | 6 | 34.8% / 45.8% | 13 | 72.2% | 1.49 px | accepted |

This directly disproves both the former `24 matches + 20 inliers` gate and the
later fixed 14-match pre-gate. Low-count poses are not accepted unconditionally:
when fewer than eight PnP inliers remain, at least five occupied grid cells and
an RMS no greater than 1.5 px are additionally required.

The replay evaluates EPNP, SQPNP and ITERATIVE for each eligible frame and keeps
the candidate with the most inliers, then the lowest RMS. EPNP won the three
low-count cases above and remains the Android runtime RANSAC initializer;
`solvePnPRefineLM` refines its inliers.

The filtered canonical database contains 4,100 records. The envelope filter
removed 900 records outside the measured bottle volume. The original SfM file
and per-observation masks are not present in the repository, so records that
fall inside the physical bottle envelope cannot be semantically proven from
repository data alone; that limitation is recorded in
`Assets/OrbModels/bottle_reference_b_manifest.json`.
