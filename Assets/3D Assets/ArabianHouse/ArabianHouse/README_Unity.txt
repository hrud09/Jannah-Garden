OLD ARABIAN HOUSE - Unity package (mobile)
==========================================
Made by make_unity_export.py from the baked mobile build (collection ArabianHouse_Mobile).

INSTALL
  Copy the whole "JannahGarden" folder into your Unity project's Assets/ folder.
  Unity 2020.3 or newer. URP (recommended for mobile) or the Built-in pipeline.
  On import, Editor/ArabianHouseImporter.cs sets everything up automatically and creates Materials/.
  Then drag Models/SM_ArabianHouse.fbx into the scene.
  If the colliders or materials are missing (e.g. scripts compiled after the model was imported):
  right-click SM_ArabianHouse.fbx -> Reimport, or run Tools > Jannah Garden > Rebuild Arabian House Materials.

WHAT YOU GET
  SM_ArabianHouse (root, LODGroup, static)
    SM_ArabianHouse_LOD0      13,071 tris  exterior shell (doorways are real openings)
    SM_ArabianHouse_Interior  10,822 tris  rooms + all furniture (drawn only with LOD0)
    SM_Door_Front_L/_R, SM_Door_Side_L/_R   152 tris each, pivot on the hinge, ArabianDoor component,
                                            kinematic Rigidbody + BoxCollider (child UCX_)
    SM_ArabianHouse_LOD1       7,601 tris  doors merged in closed, dark panel behind the upper arcade
    SM_ArabianHouse_LOD2       4,711 tris
    UCX_SM_ArabianHouse_LOD0_00..26        invisible colliders: walls with door gaps, both floors with
                                           the stair hole, stair ramp, balcony + railing, annex, hearth
  Near the house: 2 materials (exterior atlas, interior atlas), ~25k tris.
  LOD switch (screen height): LOD0 > 30%, LOD1 > 12%, LOD2 > 2%, culled below. Edit the constants at the
  top of ArabianHouseImporter.cs or the LODGroup on an instance.

  Units: metres, scale 1, rotation 0. The front (main door, balcony) faces +Z (transform.forward).

MATERIALS / TEXTURES
  URP     -> Universal Render Pipeline/Simple Lit (cheap mobile lighting), BaseColorAO + Normal
  Built-in-> Standard, BaseColorAO + Normal
  T_*_BaseColorAO  sRGB albedo with ambient occlusion already multiplied in
  T_*_Normal       tangent-space normal map, OpenGL (+Y) - same convention as Unity, no flipping needed
  2048 atlases; Android/iOS are set to ASTC 6x6 (colour) and ASTC 5x5 (normal). Drop to 1024 per platform
  in the texture import settings if you need to save memory on low-end phones.
  The FBX keeps Blender's weighted normals and tangents: the importer sets Normals = Import and
  Tangents = Import. Do not switch these to Calculate, or the soft edges and the normal map break.

DOORS
  JannahGarden.ArabianDoor on each leaf: call Open(), Close() or Toggle() from your interaction code
  (e.g. a trigger zone or a tap raycast). Both leaves swing into the house (90 degrees). The leaf's
  collider switches off while it swings so it doesn't shove the player.
