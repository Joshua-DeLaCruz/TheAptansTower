# AI log – The Aptans Tower

Weekly log of how I use AI coding agents in this project (learning activity LA9, learning objective LO6). The learning-coach agent adds an entry after each task. I fill in my own time, corrections and reflection.

## 2026-10-28 – M1 step 1: test-scene builder
**Task:** Make an Editor menu item (Tools/Aptans/Build M1 Test Scene) that builds the M1 player test scene from code, so no scene has to be built by hand.
**What the agents did:** The coordinator looked through the empty Unity 6000.4.6f1 project and proposed a six-step M1 plan, which I approved (decisions: thrown dagger with a limited count, input defined in code, I write Health/IDamageable myself, 32 PPU art with visuals on `Visual` child objects). Before the step, "Apthans" was renamed to "Aptans", the log was fixed to LA9 and a starting commit was made. The unity-gameplay agent wrote `Assets/_Project/Scripts/Editor/M1TestSceneBuilder.cs`. It creates a 32x32 placeholder sprite, adds the Ground and Player layers, builds a scene with a pixel-perfect camera, a global 2D light, floor, platforms, walls and a Player (Rigidbody2D + capsule collider), and makes it the only scene in Build Settings. The code-reviewer checked it, and the learning-coach wrote the explanation and this entry.
**Review findings:** No compile problems. Six smaller fixes, all applied: save the new layers to disk right away, re-check the sprite import settings on every run, a null check on the texture importer, two clearer comments, and a warning when the 2D Renderer is not active. Useful catch: the review found that the URP pipeline asset and 2D renderer are missing from Assets/. Looking into that showed an older copy of the project had ended up inside the `.git` folder. I would probably not have spotted this myself until the lighting looked wrong. Still being fixed. The builder has not been tested in Unity yet.
**Time and corrections:** David: ...
**Did it help my learning?** David: ...

