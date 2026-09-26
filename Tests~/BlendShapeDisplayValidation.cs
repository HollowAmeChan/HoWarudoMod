// Copy with Core/HoFaceBlendShapeDisplay.cs into a disposable Unity project's Assets/Editor.
using System;
using System.IO;
using System.Linq;
using HoFaceTracking.Core;
using UnityEditor;
using UnityEngine;

public static class BlendShapeDisplayValidation
{
    static int checks;
    public static void RunBatch()
    {
        try
        {
            if (!File.Exists(Path.Combine(Application.dataPath, "../.ho-face-validation")))
                throw new Exception("Disposable validation project marker missing");
            var character = new GameObject("Character");
            var first = Mesh(character, "Body", "JawOpen", "Smile", "<literal>", "Negative", "Wide");
            var second = Mesh(character, "Head", "JawOpen", "Smile", "Negative", "Wide");
            first.SetBlendShapeWeight(0, 20); second.SetBlendShapeWeight(0, 70);
            first.SetBlendShapeWeight(1, 35); second.SetBlendShapeWeight(1, 35);
            first.SetBlendShapeWeight(2, 8);
            first.SetBlendShapeWeight(3, -80); second.SetBlendShapeWeight(2, -10);
            first.SetBlendShapeWeight(4, 140); second.SetBlendShapeWeight(3, 110);
            using (var display = new HoFaceBlendShapeDisplay())
            {
                Update(display, character, 3);
                Check(display.RendererCount == 2 && display.Entries.Count == 5, "all meshes aggregate into unique names");
                Check(display.ConflictCount == 3, "three inconsistent names");
                Check(Entry(display, "JawOpen").Value == 70 && Entry(display, "JawOpen").Conflict, "same-name takes numeric maximum");
                Check(Entry(display, "Negative").Value == -10, "negative maximum is not absolute maximum or zero");
                Check(Entry(display, "Wide").Value == 140, "raw weights retain values above 100");
                Check(!Entry(display, "Smile").Conflict, "equal duplicate values remain white");
                var texts = display.DisplayObject.GetComponentsInChildren<TextMesh>();
                Check(texts.Count(t => t.name == "Names" && t.gameObject.activeSelf) == 2, "rows form two columns");
                Check(texts.Any(t => t.name == "Conflicts" && t.color == Color.yellow && t.text.Contains("JawOpen")), "conflicting name is yellow");
                Check(texts.Any(t => t.name == "Names" && !t.richText && t.text.Contains("<literal>")), "literal names cannot inject formatting");
                Check(!texts.Any(t => t.text.Contains("Body") || t.text.Contains("Head")), "no object names or grouping");
                Check(first.GetBlendShapeWeight(0) == 20 && second.GetBlendShapeWeight(0) == 70, "monitor never writes renderer weights");
                second.SetBlendShapeWeight(0, 20);
                Update(display, character, 2);
                Check(!Entry(display, "JawOpen").Conflict && Entry(display, "JawOpen").Value == 20, "yellow clears once values agree");
                Check(display.DisplayObject.GetComponentsInChildren<TextMesh>().Count(t => t.name == "Names" && t.gameObject.activeSelf) == 3,
                    "live row layout changes");
                UnityEngine.Object.DestroyImmediate(second.gameObject);
                Update(display, character, 24);
                Check(display.RendererCount == 1 && display.ConflictCount == 0, "removed mesh releases conflicts");
                first.sharedMesh = MakeMesh("Replacement"); first.SetBlendShapeWeight(0, 42);
                Update(display, character, 24);
                Check(display.Entries.Count == 1 && Entry(display, "Replacement").Value == 42, "mesh replacement refreshes names");
                var oldRoot = display.DisplayObject;
                character.SetActive(false); Update(display, character, 24);
                Check(display.DisplayObject == null && oldRoot == null, "inactive character cleans owned objects");
                character.SetActive(true); Update(display, character, 24);
                Check(display.DisplayObject != null, "reactivation recreates display");
                var other = new GameObject("OtherCharacter");
                Mesh(other, "OtherMesh", "New"); Update(display, other, 24);
                Check(display.Entries.Count == 1 && Entry(display, "New").Value == 0, "switching character clears previous values");
                UnityEngine.Object.DestroyImmediate(other);
                Update(display, null, 24);
                Check(display.DisplayObject == null && display.Entries.Count == 0, "null character disposes display");
            }
            UnityEngine.Object.DestroyImmediate(character);
            RenderPreview();
            Debug.Log("BLENDSHAPE_DISPLAY_PASS checks=" + checks);
            EditorApplication.Exit(0);
        }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }

    static void RenderPreview()
    {
        var character = new GameObject("Preview");
        var a = Mesh(character, "Body", "JawOpen", "MouthSmile", "EyeBlinkLeft", "EyeBlinkRight", "MouthPucker", "BrowInnerUp");
        var b = Mesh(character, "Face", "JawOpen", "MouthSmile", "EyeBlinkLeft", "EyeBlinkRight", "MouthPucker", "BrowInnerUp");
        for (int i = 0; i < 6; i++) { a.SetBlendShapeWeight(i, i * 12); b.SetBlendShapeWeight(i, i * 12); }
        b.SetBlendShapeWeight(0, 75); b.SetBlendShapeWeight(3, 55);
        var cameraObject = new GameObject("PreviewCamera");
        cameraObject.tag = "MainCamera";
        var camera = cameraObject.AddComponent<Camera>();
        camera.transform.position = new Vector3(0, 0, -3);
        camera.orthographic = true;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(.035f, .045f, .065f);
        var display = new HoFaceBlendShapeDisplay();
        display.Update(character, 3, 1.5f, 2, Vector3.zero, .1f);
        var renderers = display.DisplayObject.GetComponentsInChildren<MeshRenderer>();
        foreach (Transform column in display.DisplayObject.transform)
        {
            float nameRight = Mathf.Max(column.Find("Names").GetComponent<MeshRenderer>().bounds.max.x,
                column.Find("Conflicts").GetComponent<MeshRenderer>().bounds.max.x);
            Check(column.Find("Values").GetComponent<MeshRenderer>().bounds.min.x > nameRight,
                "rendered value column clears actual glyph bounds");
        }
        Bounds bounds = renderers[0].bounds;
        foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
        camera.transform.position = new Vector3(bounds.center.x, bounds.center.y, -3);
        camera.aspect = 2;
        camera.orthographicSize = Mathf.Max(bounds.extents.y * 1.4f, bounds.extents.x / 2 * 1.2f);
        var target = new RenderTexture(1400, 700, 24);
        camera.targetTexture = target;
        camera.Render();
        RenderTexture.active = target;
        var image = new Texture2D(1400, 700, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, 1400, 700), 0, 0); image.Apply();
        string path = Path.GetFullPath(Path.Combine(Application.dataPath, "../blendshape-display-preview.png"));
        File.WriteAllBytes(path, image.EncodeToPNG());
        Debug.Log("BLENDSHAPE_DISPLAY_PREVIEW " + path);
        RenderTexture.active = null; camera.targetTexture = null;
        target.Release(); UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(image);
        display.Dispose(); UnityEngine.Object.DestroyImmediate(character); UnityEngine.Object.DestroyImmediate(cameraObject);
    }

    static HoFaceBlendShapeDisplay.Entry Entry(HoFaceBlendShapeDisplay display, string name) => display.Entries.First(e => e.Name == name);
    static void Update(HoFaceBlendShapeDisplay display, GameObject character, int rows) => display.Update(character, rows, 1, 1.5f, Vector3.zero, .035f);
    static SkinnedMeshRenderer Mesh(GameObject parent, string objectName, params string[] names)
    {
        var go = new GameObject(objectName); go.transform.SetParent(parent.transform);
        var renderer = go.AddComponent<SkinnedMeshRenderer>();
        renderer.sharedMesh = MakeMesh(names); renderer.enabled = false;
        return renderer;
    }
    static Mesh MakeMesh(params string[] names)
    {
        var mesh = new Mesh(); mesh.vertices = new[] { Vector3.zero }; mesh.triangles = new int[0];
        foreach (string name in names) mesh.AddBlendShapeFrame(name, 100, new[] { Vector3.zero }, new[] { Vector3.zero }, new[] { Vector3.zero });
        return mesh;
    }
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks++; Debug.Log("DISPLAY_CHECK " + message); }
}
