# URP AR Prototype

This Unity project is the mobile AR prototype for the URP cultural heritage digital restoration system.

Current scope:

- Unity 2022.3 LTS Android project.
- AR Foundation and ARCore package setup.
- Prototype scene at `Assets/Scenes/UrpARPrototype.unity`.
- Imported reconstructed OBJ artifact at `Assets/Models/ReconstructedArtifact/real.obj`.
- Virtual restoration part overlay for cup/lid style demos.
- ORB tracking integration placeholder. The real ORB path will be connected after choosing an OpenCV integration route.

Near-term workflow:

1. Capture 360-degree object photos and reconstruct with Meshroom.
2. Import the reconstructed artifact body and the restoration part into `Assets/Models`.
3. Replace `Virtual Repair Part` with the final repair model.
4. Add ORB feature extraction and pose estimation.
5. Build to Android and test on a physical phone.
