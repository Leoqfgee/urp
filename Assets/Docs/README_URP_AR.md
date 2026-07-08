# URP AR Prototype

This Unity project is the mobile AR prototype for the URP cultural heritage digital restoration system.

Current scope:

- Unity 2022.3 LTS Android project.
- AR Foundation and ARCore package setup.
- Prototype scene at `Assets/Scenes/UrpARPrototype.unity`.
- Placeholder virtual restoration part for cup/lid style demos.
- ORB tracking integration placeholder. The real ORB path will be connected after choosing an OpenCV integration route.

Near-term workflow:

1. Import Meshroom/Blender exported models into `Assets/Models`.
2. Replace `Placeholder Virtual Repair Part` with the reconstructed repair model.
3. Add ORB feature extraction and pose estimation.
4. Build to Android and test on a physical phone.
