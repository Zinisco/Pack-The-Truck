using UnityEngine;

public class PieceHighlighter : MonoBehaviour
{
    [SerializeField] Material highlightMat;
    GameObject _highlightRoot;

    void Awake()
    {
        BuildHighlight();
        SetHighlight(false);
    }

    void BuildHighlight()
    {
        if (_highlightRoot != null) return;

        _highlightRoot = new GameObject("Highlight");
        _highlightRoot.transform.SetParent(transform, false);

        // MeshRenderers
        foreach (var mr in GetComponentsInChildren<MeshRenderer>(true))
        {
            var mf = mr.GetComponent<MeshFilter>();
            if (!mf) continue;

            var child = new GameObject(mr.gameObject.name + "_HL");
            child.transform.SetParent(_highlightRoot.transform, true);

            child.transform.position = mr.transform.position;
            child.transform.rotation = mr.transform.rotation;
            child.transform.localScale = mr.transform.lossyScale; // ok for most cases

            var newMF = child.AddComponent<MeshFilter>();
            newMF.sharedMesh = mf.sharedMesh;

            var newMR = child.AddComponent<MeshRenderer>();
            newMR.sharedMaterial = highlightMat;

            // optional: render above base
            newMR.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            newMR.receiveShadows = false;
        }

        // If you have SkinnedMeshRenderer pieces, handle those too:
        foreach (var smr in GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            var child = new GameObject(smr.gameObject.name + "_HL");
            child.transform.SetParent(_highlightRoot.transform, true);

            child.transform.position = smr.transform.position;
            child.transform.rotation = smr.transform.rotation;
            child.transform.localScale = smr.transform.lossyScale;

            var newSMR = child.AddComponent<SkinnedMeshRenderer>();
            newSMR.sharedMesh = smr.sharedMesh;
            newSMR.bones = smr.bones;
            newSMR.rootBone = smr.rootBone;
            newSMR.sharedMaterial = highlightMat;

            newSMR.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            newSMR.receiveShadows = false;
        }
    }

    public void SetHighlight(bool on)
    {
        if (_highlightRoot != null)
            _highlightRoot.SetActive(on);
    }
}