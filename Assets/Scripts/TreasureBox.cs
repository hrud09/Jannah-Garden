using TMPro;
using UnityEngine;
using System.Collections;

public class TreasureBox : MonoBehaviour
{

    public TreasureBoxData boxData;
    [Header("Meshes to Outline")]
    public Renderer[] meshes;

    private Material[] instancedMaterials;
    private bool isOutlineEnabled = false;


    [Header("UI References")]
    public TMP_Text nameText;
    [HideInInspector] public TreasureBoxTier tier;
    [HideInInspector] public int slotIndex;
    public Transform treasureBoxCanvas;

    [Header("Box Open Settings")]
    public GameObject boxLid;
    public float boxRiseHeight;
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

    public IEnumerator PlayOpenAnimation()
    {
        float duration = 1.5f;
        float elapsed = 0f;

        Vector3 startPos = transform.position;
        Vector3 endPos = startPos + Vector3.up * (boxRiseHeight > 0 ? boxRiseHeight : 2f);
        Vector3 startScale = transform.localScale;

        Quaternion startRot = transform.rotation;
        
        Quaternion lidStartRot = boxLid != null ? boxLid.transform.localRotation : Quaternion.identity;
        // Assume lid opens by rotating -110 on X local axis for an exaggerated open
        Quaternion lidEndRot = boxLid != null ? lidStartRot * Quaternion.Euler(-110f, 0, 0) : Quaternion.identity;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;

            // 1. Position: Anticipation (dip down) then elastic overshoot upwards
            float posT = 0f;
            if (t < 0.2f)
            {
                // Anticipation dip
                posT = Mathf.Lerp(0, -0.1f, Mathf.Sin(t / 0.2f * Mathf.PI));
            }
            else
            {
                // Elastic overshoot rise
                float rt = (t - 0.2f) / 0.8f; 
                posT = 1f - Mathf.Exp(-5f * rt) * Mathf.Cos(12f * rt);
            }
            transform.position = Vector3.LerpUnclamped(startPos, endPos, posT);

            // 2. Rotation: Smooth start, fast middle, smooth end (EaseInOut)
            float rotT = t < 0.5f ? 4f * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 3f) / 2f;
            transform.rotation = startRot * Quaternion.Euler(0, 1080f * rotT, 0);

            // 3. Scale: Squash and stretch
            float scaleY = 1f;
            float scaleXZ = 1f;
            if (t < 0.2f)
            {
                // Squash down before jumping
                scaleY = Mathf.Lerp(1f, 0.7f, Mathf.Sin(t / 0.2f * Mathf.PI));
                scaleXZ = Mathf.Lerp(1f, 1.2f, Mathf.Sin(t / 0.2f * Mathf.PI));
            }
            else if (t < 0.6f)
            {
                // Stretch up while rising fast
                float stretchT = (t - 0.2f) / 0.4f;
                scaleY = Mathf.Lerp(1f, 1.3f, Mathf.Sin(stretchT * Mathf.PI));
                scaleXZ = Mathf.Lerp(1f, 0.8f, Mathf.Sin(stretchT * Mathf.PI));
            }
            transform.localScale = new Vector3(startScale.x * scaleXZ, startScale.y * scaleY, startScale.z * scaleXZ);

            // 4. Lid: Open with an elastic bounce
            if (boxLid != null && t > 0.3f)
            {
                float lidT = (t - 0.3f) / 0.7f;
                float elasticT = 1f - Mathf.Exp(-8f * lidT) * Mathf.Cos(15f * lidT);
                boxLid.transform.localRotation = Quaternion.LerpUnclamped(lidStartRot, lidEndRot, elasticT);
            }

            yield return null;
        }

        // Final snap to ensure precision at the end of the animation
        transform.position = endPos;
        transform.rotation = startRot * Quaternion.Euler(0, 1080f, 0);
        transform.localScale = startScale;
        if (boxLid != null)
        {
            boxLid.transform.localRotation = lidEndRot;
        }
    }
}
