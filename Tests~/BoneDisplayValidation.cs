// Copy with Core/HoFaceBoneDisplay.cs into Assets/Editor of a disposable validation project.
using System;
using System.IO;
using HoFaceTracking.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class BoneDisplayValidation
{
    static int checks;
    public static void RunBatch()
    {
        try
        {
            if (!File.Exists(Path.Combine(Application.dataPath, "../.ho-face-validation")))
                throw new Exception("Disposable validation project marker missing");
            var cameraObject = new GameObject("BoneValidationCamera");
            var camera = cameraObject.AddComponent<Camera>();
            camera.transform.position = new Vector3(0, 0, -4);
            camera.nearClipPlane = .1f;
            camera.pixelRect = new Rect(0, 0, 1000, 600);
            var root = new GameObject("Character");
            root.transform.position = new Vector3(.2f, -.3f, .2f);
            root.transform.rotation = Quaternion.Euler(10, 20, 30);
            root.transform.localScale = new Vector3(1.2f, .8f, 1.1f);
            var bone = Child(root.transform, "Bone", new Vector3(.4f, .6f, 0));
            var helper = Child(bone, "Bone", new Vector3(.1f, .4f, .1f));
            helper.gameObject.SetActive(false);
            using (var display = new HoFaceBoneDisplay())
            {
                display.Update(root, false, .04f, 3, Color.cyan);
                display.BuildGeometry(camera);
                Check(display.NodeCount == 3 && display.BoneCount == 2, "full hierarchy includes inactive helpers and duplicate names");
                Check(root.GetComponentsInChildren<Transform>(true).Length == 3, "display never adds objects to the character");
                Check(display.SegmentCount == 2 && display.Geometry.vertexCount == 8, "one quad per bone link");
                var vertices = display.Geometry.vertices;
                Near((vertices[0] + vertices[1]) * .5f, root.transform.position, "root world position is not transformed twice");
                Near((vertices[2] + vertices[3]) * .5f, bone.position, "bone world position follows scaled/rotated character");
                Width(camera, vertices, 3);
                bone.localPosition += new Vector3(.1f, .2f, 0);
                display.BuildGeometry(camera);
                vertices = display.Geometry.vertices;
                Near((vertices[2] + vertices[3]) * .5f, bone.position, "live pose updates without recollection");
                display.Update(root, true, .1f, 5, Color.cyan); display.BuildGeometry(camera);
                Check(display.SegmentCount > 2 && HasColor(display.Geometry.colors, Color.red), "XYZ axes are drawn in axis colors");
                Width(camera, display.Geometry.vertices, 5);
                camera.orthographic = true; camera.orthographicSize = 2; display.BuildGeometry(camera);
                Width(camera, display.Geometry.vertices, 5);
                camera.transform.position = new Vector3(2, 1, -5); camera.transform.LookAt(root.transform);
                display.BuildGeometry(camera); Width(camera, display.Geometry.vertices, 5);
                var leaf = Child(helper, "Leaf", Vector3.up * .2f);
                display.Update(root, false, .1f, 3, Color.cyan);
                Check(display.NodeCount == 4 && display.BoneCount == 3, "new leaf is discovered automatically");
                UnityEngine.Object.DestroyImmediate(leaf.gameObject);
                display.Update(root, false, .1f, 3, Color.cyan);
                Check(display.NodeCount == 3, "removed nodes leave the collection");
                root.SetActive(false); var mesh = display.Geometry;
                display.Update(root, true, .1f, 3, Color.cyan);
                Check(display.Geometry == null && mesh == null && display.NodeCount == 0, "inactive character releases resources");
                root.SetActive(true); display.Update(root, false, .1f, 3, Color.cyan);
                var other = new GameObject("Other"); Child(other.transform, "New", Vector3.right);
                display.Update(other, false, .1f, 3, Color.cyan);
                Check(display.NodeCount == 2 && display.BoneCount == 1, "switching character clears the previous hierarchy");
                UnityEngine.Object.DestroyImmediate(other);
                display.Update(null, true, .1f, 3, Color.cyan);
                Check(display.Geometry == null && display.NodeCount == 0, "null target disposes resources");
            }
            UnityEngine.Object.DestroyImmediate(root);
            camera.orthographic = false; camera.transform.position = Vector3.zero; camera.transform.rotation = Quaternion.identity;
            NearPlaneAndLargeRig(camera);
            UnityEngine.Object.DestroyImmediate(cameraObject);
            RenderPreview();
            Debug.Log("BONE_DISPLAY_PASS checks=" + checks);
            EditorApplication.Exit(0);
        }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }

    static void NearPlaneAndLargeRig(Camera camera)
    {
        var root = new GameObject("NearPlane"); root.transform.position = new Vector3(-.1f, 0, -.3f);
        var tip = Child(root.transform, "Tip", new Vector3(.3f, .2f, 1));
        using (var display = new HoFaceBoneDisplay())
        {
            display.Update(root, false, 0, 3, Color.cyan); display.BuildGeometry(camera);
            Check(display.SegmentCount == 1, "near-plane crossing is clipped instead of discarded");
            foreach (var vertex in display.Geometry.vertices)
                Check(camera.WorldToScreenPoint(vertex).z >= camera.nearClipPlane - 1e-5f, "clipped vertex stays in front of near plane");
            tip.localPosition = new Vector3(.1f, .1f, -.2f); display.BuildGeometry(camera);
            Check(display.SegmentCount == 0 && display.Geometry.vertexCount == 0, "behind-camera segments are omitted");
        }
        UnityEngine.Object.DestroyImmediate(root);
        root = new GameObject("LargeRig"); root.transform.position = new Vector3(0, 0, 4);
        for (int i = 0; i < 5500; i++) Child(root.transform, "Bone", new Vector3((i % 100 + 1) * .005f, (i / 100 + 1) * .005f, 0));
        using (var display = new HoFaceBoneDisplay())
        {
            display.Update(root, true, .04f, 2, Color.cyan); display.BuildGeometry(camera);
            Check(display.NodeCount == 5501 && display.Geometry.vertexCount > 65535 && display.Geometry.indexFormat == IndexFormat.UInt32,
                "large full hierarchy supports more than 65535 vertices");
        }
        UnityEngine.Object.DestroyImmediate(root);
    }

    static void RenderPreview()
    {
        var root = new GameObject("PreviewSkeleton");
        root.transform.rotation = Quaternion.Euler(0, 20, 0);
        var hips = Child(root.transform, "Hips", new Vector3(0, .8f, 0));
        var spine = Child(hips, "Spine", new Vector3(0, .3f, 0));
        var chest = Child(spine, "Chest", new Vector3(0, .3f, 0));
        var neck = Child(chest, "Neck", new Vector3(0, .15f, 0));
        Child(neck, "Head", new Vector3(0, .22f, 0));
        foreach (int sign in new[] { -1, 1 })
        {
            var shoulder = Child(chest, "Shoulder", new Vector3(sign * .25f, .05f, 0));
            var elbow = Child(shoulder, "Elbow", new Vector3(sign * .32f, -.15f, 0));
            Child(elbow, "Hand", new Vector3(sign * .28f, -.2f, -.05f));
            var leg = Child(hips, "UpperLeg", new Vector3(sign * .15f, -.1f, 0));
            var knee = Child(leg, "Knee", new Vector3(0, -.33f, -.04f));
            var ankle = Child(knee, "Ankle", new Vector3(0, -.32f, .04f));
            Child(ankle, "Foot", new Vector3(0, -.05f, -.15f));
        }
        var occluder = GameObject.CreatePrimitive(PrimitiveType.Cube);
        occluder.transform.position = new Vector3(0, .92f, -.35f);
        occluder.transform.localScale = new Vector3(.5f, 1.05f, .2f);
        var matte = new Material(Shader.Find("Unlit/Color")); matte.color = new Color(.12f, .16f, .22f);
        occluder.GetComponent<Renderer>().sharedMaterial = matte;
        var go = new GameObject("Camera"); var camera = go.AddComponent<Camera>();
        camera.transform.position = new Vector3(0, .9f, -4);
        camera.orthographic = true; camera.orthographicSize = 1.1f;
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.035f, .045f, .065f);
        var target = new RenderTexture(1100, 900, 24); camera.targetTexture = target;
        using (var display = new HoFaceBoneDisplay())
        {
            display.Update(root, true, .09f, 3, new Color(.2f, .75f, 1, 1));
            camera.Render();
            var image = Read(target);
            int drawn = ColoredPixels(image);
            Check(drawn > 2000, "camera callback renders colored geometry including axes");
            // The spine is physically behind an opaque cube; overlay must remain visible.
            Vector3 midpoint = (spine.position + chest.position) * .5f;
            Vector3 pixel = camera.WorldToScreenPoint(midpoint);
            Color center = image.GetPixel((int)pixel.x, (int)pixel.y);
            Check(center.g > .5f && center.b > .7f, "bones render through opaque character geometry");
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "../bone-display-preview.png"));
            File.WriteAllBytes(path, image.EncodeToPNG()); Debug.Log("BONE_DISPLAY_PREVIEW " + path);
            UnityEngine.Object.DestroyImmediate(image);
            camera.cullingMask = 0; camera.Render(); image = Read(target);
            Check(ColoredPixels(image) == 0, "camera layer mask is respected");
            UnityEngine.Object.DestroyImmediate(image); camera.cullingMask = -1;
            display.Dispose(); camera.Render(); image = Read(target);
            Check(ColoredPixels(image) == 0, "dispose removes camera callback and all overlay drawing");
            UnityEngine.Object.DestroyImmediate(image);
        }
        camera.targetTexture = null; target.Release();
        UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(go);
        UnityEngine.Object.DestroyImmediate(occluder); UnityEngine.Object.DestroyImmediate(matte); UnityEngine.Object.DestroyImmediate(root);
    }

    static Texture2D Read(RenderTexture target)
    {
        RenderTexture.active = target; var image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); image.Apply(); RenderTexture.active = null; return image;
    }
    static int ColoredPixels(Texture2D image)
    {
        int count = 0;
        foreach (Color c in image.GetPixels()) if (Mathf.Max(c.r, Mathf.Max(c.g, c.b)) > .5f) count++;
        return count;
    }
    static Transform Child(Transform parent, string name, Vector3 local)
    {
        var child = new GameObject(name).transform; child.SetParent(parent, false); child.localPosition = local; return child;
    }
    static bool HasColor(Color[] colors, Color expected)
    {
        foreach (var color in colors) if (color.r > .9f && color.g < .3f && color.b < .3f) return true;
        return false;
    }
    static void Width(Camera camera, Vector3[] vertices, float expected)
    {
        float width = Vector2.Distance(camera.WorldToScreenPoint(vertices[0]), camera.WorldToScreenPoint(vertices[1]));
        Check(Mathf.Abs(width - expected) < .01f, "pixel width matches rendering camera (" + width + ")");
    }
    static void Near(Vector3 a, Vector3 b, string message) => Check(Vector3.Distance(a, b) < 1e-4f, message);
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks++; Debug.Log("BONE_CHECK " + message); }
}
