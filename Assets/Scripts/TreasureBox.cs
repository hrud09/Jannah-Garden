using TMPro;
using UnityEngine;

public class TreasureBox : MonoBehaviour
{

    public TreasureBoxRewardData rewardData;
    [Header("Meshes to Outline")]
    public Renderer[] meshes;

    private Material[] instancedMaterials;
    private bool isOutlineEnabled = false;


    [Header("UI References")]
    public TMP_Text nameText;
    public TMP_Text timerText;
    [HideInInspector] public TreasureBoxTier tier;
    [HideInInspector] public int slotIndex;
    public Transform treasureBoxCanvas;
    private void Start()
    {
        // Cache instanced materials to avoid cloning materials repeatedly at runtime.
        // Reading renderer.material in Unity automatically clones the material to prevent
        // changes from affecting other objects using the same shared material.
        if (meshes != null)
        {
            instancedMaterials = new Material[meshes.Length];
            for (int i = 0; i < meshes.Length; i++)
            {
                if (meshes[i] != null)
                {
                    instancedMaterials[i] = meshes[i].material;
                }
            }
        }
    }

    private void LateUpdate()
    {
        // Make the canvas face the camera
        if (treasureBoxCanvas != null)
        {
            Camera mainCamera = Camera.main;
            if (mainCamera != null)
            {
                treasureBoxCanvas.LookAt(mainCamera.transform);
            }
        }
    }

    /// <summary>
    /// Configures the outline pass and shader keywords on cached materials.
    /// Compatible with OmniShade URP shader properties.
    /// </summary>
    public void SetOutline(bool enable)
    {
        if (isOutlineEnabled == enable) return;
        isOutlineEnabled = enable;

        if (instancedMaterials == null) return;

        foreach (Material mat in instancedMaterials)
        {
            if (mat == null) continue;

            // Set URP outline toggle float property
            mat.SetFloat("_Outline", enable ? 1f : 0f);

            // Enable or disable the shader pass ("SRPDefaultUnlit" for URP, "Always" for Built-in)
            string outlinePassName = mat.shader.name.Contains("URP") ? "SRPDefaultUnlit" : "Always";
            mat.SetShaderPassEnabled(outlinePassName, enable);

            // Set OmniShade keywords corresponding to outline status
            if (enable)
            {
                mat.EnableKeyword("OUTLINE");
                mat.DisableKeyword("OUTLINE_PASS_DISABLED");
            }
            else
            {
                mat.DisableKeyword("OUTLINE");
                mat.EnableKeyword("OUTLINE_PASS_DISABLED");
            }

            // Adjust stencils if Interior Outlines is set to Hide (value 6)
            if (mat.HasProperty("_OutlineComp") && mat.GetInt("_OutlineComp") == 6)
            {
                if (mat.HasProperty("_OutlineGroup") && mat.GetInt("_OutlineGroup") == 0)
                {
                    mat.SetInt("_OutlineGroup", 1);
                }
                mat.SetInt("_OutlinePass", enable ? 2 : 0);
            }
            else
            {
                mat.SetInt("_OutlinePass", 0);
            }
        }
    }

    private void OnDestroy()
    {
        // Destroy instanced materials to prevent memory leaks when the object is destroyed
        if (instancedMaterials != null)
        {
            foreach (Material mat in instancedMaterials)
            {
                if (mat != null)
                {
                    Destroy(mat);
                }
            }
        }
    }
}
