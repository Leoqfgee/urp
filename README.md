# URP AR Prototype

This Unity project is the summer-task prototype for the cultural heritage digital restoration and AR presentation system.

Current build focus:

- Android AR app built with Unity 2022.3 LTS and AR Foundation.
- ORB feature matching through the locally installed `OpenCV plus Unity` Asset Store plugin.
- A cropped real-object target image at `Assets/Textures/Targets/orb_target.jpg`.
- The reconstructed artifact model plus a virtual repair overlay are shown only after ORB finds the target.

Local dependency:

- Install `OpenCV plus Unity` from Unity Asset Store before opening or building this project.
- The plugin folder is intentionally ignored by Git because Asset Store packages should not be redistributed as raw plugin source in this repository.

Build:

- Unity menu: `URP AR/Setup Prototype Scene`
- Command-line build method: `Urp.ArDemo.Editor.UrpArProjectSetup.BuildAndroidFromCommandLine`
- APK output: `Builds/urp-ar-demo.apk`

Important limitation:

- The free OpenCV plugin imported locally only includes Android `armeabi-v7a` native libraries, so this prototype build targets ARMv7 for testing. A production Android app should use an OpenCV package or native build that includes `arm64-v8a`.
