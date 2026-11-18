PEGASUS - Unity import notes (mobile)
=====================================

Files
-----
Pegasus.fbx           skinned mesh + skeleton (47 bones, single root "root") + 4 animation clips
Textures/             body / feathers / harness diffuse + normal maps (1024x1024 PNG)

Model: ~10.5k triangles, 4 meshes (Body, Wings, Hair, Harness), max 4 bone influences per vertex.
Facing: +Z in Unity (forward). Size: real scale, ~2.1 m tall to the ear tips.

Animation clips (24 fps, all loop seamlessly, all "in place" - no root motion)
--------------------------------------------------------------------------------
Pegasus_Idle       frames 1-121  (5.0 s)   breathing, looks around, tail swishes, wing rouse
Pegasus_Walk       frames 1-33   (1.33 s)  4-beat walk   -> move the character at ~0.94 m/s
Pegasus_Run        frames 1-13   (0.5 s)   gallop, wings held back -> move at ~7.2 m/s
Pegasus_Run_Flap   frames 1-13   (0.5 s)   same gallop with one wing beat per stride -> ~7.2 m/s
(The last frame equals the first one; keep the full range so the loop period is exact.)

Recommended import settings
---------------------------
Model tab
  - Scale Factor 1, Convert Units ON
  - Bake Axis Conversion ON   (removes the -90 X rotation Blender exports put on the root)
  - Read/Write Enabled OFF (saves memory on mobile)
  - Mesh Compression: Low or Medium
  - Normals: Import, Tangents: Calculate Mikktspace (normal maps)
Rig tab
  - Animation Type: Generic
  - Avatar Definition: Create From This Model, Root node: root
  - Skin Weights: Standard (4 bones). On very low-end devices you can drop to 2 bones in Quality settings.
  - Optimize Game Objects: ON (strips the bone transforms from the hierarchy -> cheaper on mobile)
Animation tab
  - For every clip: Loop Time ON, Loop Pose OFF (the clips already loop exactly)
  - Anim. Compression: Optimal
  - Root Transform Rotation/Position: Bake Into Pose (clips are in place)
Materials tab
  - Location: Use External Materials (Legacy) or Extract Materials..., then assign the textures in
    Textures/ (Unity usually finds them automatically by name).
  - Hair material has no texture (plain colour) - give it a light colour that matches the mane.
Textures
  - *_normal.png: Texture Type = Normal map
  - Android: ASTC 6x6 (or 8x8 for smaller size), iOS: ASTC 6x6; Max Size 1024 (512 also looks fine on phones)

Notes
-----
- The wing feathers are single-sided cards in the source model; if they disappear from one side,
  use a double-sided / Cull Off material for feathers_Mat.
- Animator: suggested states Idle <-> Walk <-> Run (or Run_Flap), blend by speed.
- Source file: main.blend (rig with IK controls, all actions). Backup of the original model:
  main_backup_before_rig.blend
