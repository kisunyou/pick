using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class RobotBearBuilder
{
    const string Source = "Assets/Resources/Prefabs/dollPrefabs/doll_bear_full_prefab.prefab";
    const string Folder = "Assets/Resources/Model/RobotBear";
    const string Destination = "Assets/Resources/Prefabs/dollPrefabs/doll_bear2_full_prefab.prefab";
    static readonly List<Vector3> Vertices = new List<Vector3>();
    static readonly List<Vector3> Normals = new List<Vector3>();
    static readonly List<Vector2> UVs = new List<Vector2>();
    static readonly List<int> Triangles = new List<int>();
    static readonly List<BoneWeight> Weights = new List<BoneWeight>();
    static Transform meshSpace;

    [MenuItem("Tools/Robot Bear/Update animated bounds")]
    public static void UpdateBounds()
    {
        var root=PrefabUtility.LoadPrefabContents(Destination);
        try
        {
            var skin=root.GetComponentInChildren<SkinnedMeshRenderer>(true);
            skin.localBounds=AnimatedBounds(skin);
            PrefabUtility.SaveAsPrefabAsset(root,Destination);
        }
        finally{PrefabUtility.UnloadPrefabContents(root);}
    }
    static Bounds AnimatedBounds(SkinnedMeshRenderer skin)
    {
        var animator=skin.GetComponentInParent<Animator>();
        var transforms=animator.GetComponentsInChildren<Transform>(true);
        var positions=transforms.Select(t=>t.localPosition).ToArray();var rotations=transforms.Select(t=>t.localRotation).ToArray();var scales=transforms.Select(t=>t.localScale).ToArray();
        var baked=new Mesh();Bounds bounds=default;bool first=true;int samples=0;
        Action restore=()=>{for(int i=0;i<transforms.Length;i++){transforms[i].localPosition=positions[i];transforms[i].localRotation=rotations[i];transforms[i].localScale=scales[i];}};
        Action capture=()=>{skin.BakeMesh(baked);foreach(var vertex in baked.vertices){var point=skin.rootBone.InverseTransformPoint(skin.transform.TransformPoint(vertex));if(first){bounds=new Bounds(point,Vector3.zero);first=false;}else bounds.Encapsulate(point);}samples++;};
        try
        {
            capture();
            foreach(var clip in animator.runtimeAnimatorController.animationClips.Distinct())for(int i=0;i<=8;i++)
            {restore();clip.SampleAnimation(animator.gameObject,clip.length*i/8f);capture();}
            bounds.Expand(.10f);
            File.AppendAllText("_report/robot_bear_validation.txt",$"Animated bounds: {samples} rest/animation samples captured in root-bone space.\n");
            return bounds;
        }
        finally{restore();UnityEngine.Object.DestroyImmediate(baked);}
    }

    [MenuItem("Tools/Robot Bear/Render and validate variant")]
    public static void Preview()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Use Edit Mode.");
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(Destination);
        var original=AssetDatabase.LoadAssetAtPath<GameObject>(Source);
        if(PrefabUtility.GetPrefabAssetType(prefab)!=PrefabAssetType.Variant)throw new Exception("Expected a prefab variant.");
        var originalSkin=original.GetComponentInChildren<SkinnedMeshRenderer>(true);
        var skin=prefab.GetComponentInChildren<SkinnedMeshRenderer>(true);
        if(skin.sharedMaterial.shader!=originalSkin.sharedMaterial.shader)throw new Exception("Shader changed.");
        if(skin.sharedMesh==originalSkin.sharedMesh || skin.sharedMaterial.mainTexture==originalSkin.sharedMaterial.mainTexture)throw new Exception("Model and texture must be different assets.");
        if(skin.bones.Length!=skin.sharedMesh.bindposes.Length)throw new Exception("Invalid bone binding.");
        if(prefab.GetComponentInChildren<Animator>(true).runtimeAnimatorController!=original.GetComponentInChildren<Animator>(true).runtimeAnimatorController)throw new Exception("Animation controller changed.");
        if(prefab.GetComponent<FunRabbit.Actor>().CollidersArray.Any(c=>c==null))throw new Exception("Missing collider reference.");
        foreach(var weight in skin.sharedMesh.boneWeights)if(weight.weight0!=1 || weight.boneIndex0>=skin.bones.Length)throw new Exception("Invalid skin weights.");
        RenderPrefab(prefab,"_report/doll_bear2_robot_preview.png",new Vector3(1.4f,.65f,2.8f));
        RenderPrefab(prefab,"_report/doll_bear2_robot_rear.png",new Vector3(-1.4f,.65f,-2.8f));
        var attack=prefab.GetComponentInChildren<Animator>(true).runtimeAnimatorController.animationClips.FirstOrDefault(c=>c.name.ToLowerInvariant().Contains("attack"));
        if(attack!=null)RenderPrefab(prefab,"_report/doll_bear2_robot_attack.png",new Vector3(1.4f,.65f,2.8f),attack);
        RenderPrefab(original,".utmp/doll_bear_original_preview.png",new Vector3(1.4f,.65f,2.8f));
        File.AppendAllText("_report/robot_bear_validation.txt","PASS: variant inheritance, same shader, independent mesh/texture, animation controller, bone weights, and collider references.\n");
    }
    static void RenderPrefab(GameObject prefab,string path,Vector3 angle,AnimationClip pose=null)
    {
        var preview=new PreviewRenderUtility();
        try
        {
            var instance=UnityEngine.Object.Instantiate(prefab);preview.AddSingleGO(instance);
            foreach(var behaviour in instance.GetComponentsInChildren<MonoBehaviour>(true))behaviour.enabled=false;
            var skin=instance.GetComponentInChildren<SkinnedMeshRenderer>(true);
            var animator=instance.GetComponentInChildren<Animator>(true);
            if(pose!=null)pose.SampleAnimation(animator.gameObject,pose.length*.4f);
            var baked=new Mesh();skin.BakeMesh(baked);
            var vertices=baked.vertices;
            if(vertices.Any(v=>float.IsNaN(v.x)||float.IsNaN(v.y)||float.IsNaN(v.z)))throw new Exception("Invalid posed vertex.");
            float delta=0;var rest=skin.sharedMesh.vertices;
            for(int i=0;i<vertices.Length;i++)delta=Mathf.Max(delta,Vector3.Distance(vertices[i],rest[i]));
            if(pose==null && delta>.001f)throw new Exception("Skinned rest pose differs from the static mesh.");
            if(pose!=null && delta<.001f)throw new Exception("Animation did not move the mesh.");
            if(pose!=null)File.AppendAllText("_report/robot_bear_validation.txt",$"PASS: {pose.name} sampled; maximum vertex displacement={delta:F4}.\n");
            Bounds bounds=new Bounds(skin.transform.TransformPoint(vertices[0]),Vector3.zero);
            foreach(var vertex in vertices)bounds.Encapsulate(skin.transform.TransformPoint(vertex));
            var camera=preview.camera;camera.orthographic=true;camera.orthographicSize=.72f;
            camera.transform.position=bounds.center+angle;camera.transform.LookAt(bounds.center);
            camera.nearClipPlane=.01f;camera.farClipPlane=20;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.15f,.20f,.23f,1);
            preview.lights[0].intensity=1.25f;preview.lights[0].transform.rotation=Quaternion.Euler(35,-35,0);
            preview.lights[1].intensity=.6f;preview.lights[1].transform.rotation=Quaternion.Euler(20,150,0);
            preview.ambientColor=new Color(.5f,.5f,.5f,1);
            preview.BeginStaticPreview(new Rect(0,0,1024,1024));preview.Render(true);
            var image=preview.EndStaticPreview();File.WriteAllBytes(path,image.EncodeToPNG());UnityEngine.Object.DestroyImmediate(image);UnityEngine.Object.DestroyImmediate(baked);
        }
        finally{preview.Cleanup();}
    }

    [MenuItem("Tools/Robot Bear/Create robot variant")]
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Use Edit Mode.");
        if (AssetDatabase.LoadAssetAtPath<GameObject>(Destination) != null)
            throw new InvalidOperationException("Robot variant already exists; refusing to overwrite an edited asset.");
        var scene = EditorSceneManager.NewPreviewScene();
        GameObject root = null;
        try
        {
            Directory.CreateDirectory(Folder); AssetDatabase.Refresh();
            root = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Source), scene);
            root.name = "doll_bear2_full_prefab";
            var skin = root.GetComponentInChildren<SkinnedMeshRenderer>(true);
            meshSpace = skin.transform;
            Vertices.Clear(); Normals.Clear(); UVs.Clear(); Triangles.Clear(); Weights.Clear();

            // Coordinates are in the original prefab's space (+Z is the face).
            // Rigid weights retain the original bear's animated bone paths.
            Box(new Vector3(0,-.215f,-.045f), new Vector3(.46f,.38f,.38f), .075f, 1, 0);
            Box(new Vector3(0,-.20f,.15f), new Vector3(.34f,.25f,.055f), .035f, 0, 0);
            Box(new Vector3(0,-.14f,.186f), new Vector3(.14f,.072f,.022f), .012f, 2, 0);
            for(int i=0;i<3;i++) Box(new Vector3(-.042f+i*.042f,-.14f,.201f),new Vector3(.027f,.044f,.012f),.005f,3,0);
            for(int i=0;i<3;i++) Box(new Vector3(0,-.222f-i*.024f,.185f),new Vector3(.17f,.009f,.016f),.003f,2,0);
            Box(new Vector3(0,-.065f,-.015f),new Vector3(.20f,.10f,.20f),.025f,2,0);
            // Large softened head, inset visor, and a bear muzzle with grille.
            Box(new Vector3(0,.232f,-.028f), new Vector3(.68f,.465f,.47f), .09f, 0, 1);
            Box(new Vector3(0,.248f,.214f), new Vector3(.57f,.23f,.055f), .05f, 1, 1);
            Box(new Vector3(0,.255f,.246f), new Vector3(.515f,.17f,.035f), .038f, 2, 1);
            for(int side=-1;side<=1;side+=2)
            {
                int eye = side<0 ? 2 : 8;
                Box(new Vector3(side*.166f,.265f,.269f),new Vector3(.082f,.070f,.017f),.015f,3,eye);
                Box(new Vector3(side*.174f,.283f,.280f),new Vector3(.036f,.011f,.006f),.003f,7,eye);
                Box(new Vector3(side*.26f,.156f,.254f),new Vector3(.055f,.020f,.018f),.006f,4,1);
                // Radial bear ears; front discs and dark hub rings.
                int ear = side<0 ? 9 : 10;
                Cylinder(new Vector3(side*.278f,.485f,-.062f),.103f,.12f,0,ear);
                Cylinder(new Vector3(side*.278f,.485f,.005f),.077f,.025f,1,ear);
                Cylinder(new Vector3(side*.278f,.485f,.024f),.051f,.018f,4,ear);
                Cylinder(new Vector3(side*.278f,.485f,.036f),.023f,.012f,5,ear);
                int arm=side<0?4:6; int leg=side<0?5:7;
                Box(new Vector3(side*.285f,-.098f,-.055f),new Vector3(.15f,.16f,.17f),.045f,2,arm);
                Box(new Vector3(side*.318f,-.133f,.068f),new Vector3(.18f,.235f,.255f),.04f,0,arm);
                Box(new Vector3(side*.319f,-.084f,.2f),new Vector3(.11f,.036f,.023f),.008f,4,arm);
                Box(new Vector3(side*.318f,-.212f,.086f),new Vector3(.186f,.045f,.255f),.012f,1,arm);
                Box(new Vector3(side*.318f,-.224f,.218f),new Vector3(.145f,.092f,.105f),.02f,1,arm);
                for(int i=0;i<2;i++) Box(new Vector3(side*.318f+(i==0?-.026f:.026f),-.224f,.273f),new Vector3(.009f,.056f,.009f),.002f,2,arm);
                Box(new Vector3(side*.205f,-.332f,.055f),new Vector3(.125f,.14f,.16f),.026f,2,leg);
                Box(new Vector3(side*.225f,-.409f,.126f),new Vector3(.255f,.16f,.30f),.04f,0,leg);
                Box(new Vector3(side*.225f,-.471f,.132f),new Vector3(.26f,.041f,.305f),.012f,1,leg);
                Box(new Vector3(side*.225f,-.403f,.281f),new Vector3(.16f,.04f,.018f),.008f,4,leg);
            }
            Box(new Vector3(0,.111f,.273f),new Vector3(.29f,.128f,.14f),.036f,6,3);
            Box(new Vector3(0,.155f,.348f),new Vector3(.088f,.045f,.025f),.013f,2,3);
            for(int i=0;i<4;i++) Box(new Vector3(-.066f+i*.044f,.089f,.347f),new Vector3(.022f,.028f,.015f),.005f,2,3);
            // Back service panel, spine stripes, and a small antenna.
            Box(new Vector3(0,.228f,-.27f),new Vector3(.36f,.26f,.04f),.027f,1,1);
            for(int i=0;i<4;i++) Box(new Vector3(0,.28f-i*.036f,-.295f),new Vector3(.20f,.013f,.014f),.003f,2,1);
            Box(new Vector3(.11f,.481f,-.1f),new Vector3(.022f,.084f,.022f),.006f,1,1);
            Box(new Vector3(.11f,.529f,-.1f),new Vector3(.047f,.037f,.047f),.012f,3,1);

            var mesh = new Mesh { name = "doll_bear2_robot_mesh" };
            mesh.SetVertices(Vertices); mesh.SetNormals(Normals); mesh.SetUVs(0, UVs); mesh.SetTriangles(Triangles,0);
            mesh.boneWeights = Weights.ToArray(); mesh.bindposes = skin.sharedMesh.bindposes;
            mesh.RecalculateBounds(); mesh.RecalculateTangents();
            AssetDatabase.CreateAsset(mesh, Folder + "/doll_bear2_robot_mesh.asset");
            var texture = MakeTexture();
            var material = new Material(skin.sharedMaterial) { name="doll_bear2_robot" };
            material.SetTexture("_BaseMap",texture); material.SetColor("_BaseColor", Color.white);
            if(material.HasProperty("_MainTex")) material.SetTexture("_MainTex",texture);
            material.SetFloat("_Saturation",1.05f); material.SetFloat("_Brightness",1.05f);
            material.SetFloat("_SpecStrength",.38f); material.SetFloat("_RimStrength",.28f);
            AssetDatabase.CreateAsset(material, Folder+"/doll_bear2_robot.mat");
            skin.sharedMesh=mesh; skin.sharedMaterials=new[]{material}; skin.localBounds=AnimatedBounds(skin);
            foreach(var renderer in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                var filter=renderer.GetComponent<MeshFilter>();
                if(filter==null)continue;
                // The source static and skinned renderers share a local frame.
                if ((renderer.transform.localToWorldMatrix.GetColumn(0)-skin.transform.localToWorldMatrix.GetColumn(0)).sqrMagnitude>.0001f)
                    throw new InvalidOperationException("Unexpected source renderer transform.");
                filter.sharedMesh=mesh; renderer.sharedMaterials=new[]{material};
            }
            foreach(var collider in root.GetComponentsInChildren<Collider>(true))
            {
                if(collider.gameObject.name.StartsWith("Hull")) UnityEngine.Object.DestroyImmediate(collider.gameObject);
                else UnityEngine.Object.DestroyImmediate(collider);
            }
            var colliderRoot=new GameObject("Robot collision"); colliderRoot.layer=root.layer; colliderRoot.transform.SetParent(root.transform,false);
            var colliders = new List<Collider>();
            Action<Vector3,Vector3> collision=(center,size)=>{var box=colliderRoot.AddComponent<BoxCollider>();box.center=center;box.size=size;colliders.Add(box);};
            collision(new Vector3(0,.232f,-.028f),new Vector3(.65f,.43f,.45f));
            collision(new Vector3(0,-.21f,-.03f),new Vector3(.44f,.36f,.37f));
            collision(new Vector3(0,.12f,.285f),new Vector3(.27f,.12f,.12f));
            for(int side=-1;side<=1;side+=2)
            {
                collision(new Vector3(side*.318f,-.14f,.08f),new Vector3(.17f,.23f,.26f));
                collision(new Vector3(side*.225f,-.409f,.126f),new Vector3(.25f,.16f,.30f));
                var sphere=colliderRoot.AddComponent<SphereCollider>();sphere.center=new Vector3(side*.278f,.485f,-.062f);sphere.radius=.098f;colliders.Add(sphere);
            }
            var actor=root.GetComponent<FunRabbit.Actor>();
            var serialized=new SerializedObject(actor); var refs=serialized.FindProperty("colliders"); refs.arraySize=colliders.Count;
            for(int i=0;i<colliders.Count;i++)refs.GetArrayElementAtIndex(i).objectReferenceValue=colliders[i];
            serialized.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root,Destination); AssetDatabase.SaveAssets();
            Directory.CreateDirectory("_report");
            File.WriteAllText("_report/robot_bear_validation.txt",$"Created {Destination}\nVertices={mesh.vertexCount}; triangles={mesh.triangles.Length/3}; bones={skin.bones.Length}; colliders={colliders.Count}\nShader={material.shader.name}\nSource prefab, mesh, and material remain unchanged.\n");
        }
        catch(Exception e){File.WriteAllText(".utmp/robot_bear_error.txt",e.ToString());throw;}
        finally {EditorSceneManager.ClosePreviewScene(scene);meshSpace=null;}
    }

    static Vector2 Atlas(Vector2 uv,int tile) => new Vector2(((tile%4)+.07f+uv.x*.86f)/4f,((tile/4)+.07f+uv.y*.86f)/2f);
    static void Vertex(Vector3 point,Vector3 normal,Vector2 uv,int tile,int bone)
    {
        Vertices.Add(meshSpace.InverseTransformPoint(point)); Normals.Add(meshSpace.InverseTransformDirection(normal).normalized);
        UVs.Add(Atlas(uv,tile)); Weights.Add(new BoneWeight{boneIndex0=bone,weight0=1});
    }
    static void Box(Vector3 center,Vector3 size,float radius,int tile,int bone)
    {
        Vector3 half=size*.5f, inner=half-Vector3.one*radius;
        for(int axis=0;axis<3;axis++)for(int sign=-1;sign<=1;sign+=2)
        {
            int a=(axis+1)%3,b=(axis+2)%3; int start=Vertices.Count;
            float[] aa={-half[a],-inner[a],0,inner[a],half[a]}; float[] bb={-half[b],-inner[b],0,inner[b],half[b]};
            for(int y=0;y<5;y++)for(int x=0;x<5;x++)
            {
                Vector3 cube=Vector3.zero;cube[axis]=sign*half[axis];cube[a]=aa[x];cube[b]=bb[y];
                Vector3 clamped=new Vector3(Mathf.Clamp(cube.x,-inner.x,inner.x),Mathf.Clamp(cube.y,-inner.y,inner.y),Mathf.Clamp(cube.z,-inner.z,inner.z));
                Vector3 normal=(cube-clamped).normalized;
                Vertex(center+clamped+normal*radius,normal,new Vector2((aa[x]/half[a]+1)*.5f,(bb[y]/half[b]+1)*.5f),tile,bone);
            }
            for(int y=0;y<4;y++)for(int x=0;x<4;x++)
            {
                int i=start+y*5+x;
                if(sign>0)Triangles.AddRange(new[]{i,i+1,i+6,i,i+6,i+5});
                else Triangles.AddRange(new[]{i,i+6,i+1,i,i+5,i+6});
            }
        }
    }
    static void Cylinder(Vector3 center,float radius,float depth,int tile,int bone)
    {
        const int sides=20; float bevel=Mathf.Min(.012f,depth*.25f);
        float[] zs={-depth*.5f,-depth*.5f+bevel,depth*.5f-bevel,depth*.5f};float[] rs={radius-bevel,radius,radius,radius-bevel};
        for(int ring=0;ring<3;ring++)for(int i=0;i<sides;i++)
        {
            int start=Vertices.Count;
            for(int j=0;j<4;j++)
            {
                int step=i+(j==1||j==2?1:0), k=ring+(j>=2?1:0);float angle=step*Mathf.PI*2/sides;
                Vector3 radial=new Vector3(Mathf.Cos(angle),Mathf.Sin(angle),0);
                Vector3 normal=(radial+Vector3.forward*(ring==0?-1: ring==2?1:0)).normalized;
                Vertex(center+radial*rs[k]+Vector3.forward*zs[k],normal,new Vector2((float)step/sides,(float)k/3),tile,bone);
            }
            Triangles.AddRange(new[]{start,start+1,start+2,start,start+2,start+3});
        }
        for(int sign=-1;sign<=1;sign+=2)for(int i=0;i<sides;i++)
        {
            int start=Vertices.Count;
            Vertex(center+Vector3.forward*depth*.5f*sign,Vector3.forward*sign,new Vector2(.5f,.5f),tile,bone);
            for(int j=0;j<2;j++){float angle=(i+j)*Mathf.PI*2/sides;Vector3 d=new Vector3(Mathf.Cos(angle),Mathf.Sin(angle),0);Vertex(center+d*(radius-bevel)+Vector3.forward*depth*.5f*sign,Vector3.forward*sign,new Vector2(.5f+d.x*.5f,.5f+d.y*.5f),tile,bone);}
            Triangles.AddRange(sign>0?new[]{start,start+1,start+2}:new[]{start,start+2,start+1});
        }
    }
    static Texture2D MakeTexture()
    {
        const int n=256;var tex=new Texture2D(n*4,n*2,TextureFormat.RGBA32,false);
        string[] hex={"D6DDD3","35646C","132632","5FE2DA","EB7845","CCA564","A1BEC0","EEFFEE"};
        var pixels=new Color[n*n*8];
        for(int tile=0;tile<8;tile++)
        {
            ColorUtility.TryParseHtmlString("#"+hex[tile],out Color color);
            for(int y=0;y<n;y++)for(int x=0;x<n;x++)
            {
                float u=x/(float)(n-1),v=y/(float)(n-1),edge=Mathf.Min(Mathf.Min(u,1-u),Mathf.Min(v,1-v));
                float brush=Mathf.Sin(y*2.13f+x*.09f)*.007f+Mathf.Sin(y*.27f)*.006f;
                float shade=.96f+.055f*v+brush;
                if(tile==0||tile==1||tile==6){if(edge>.086f&&edge<.10f)shade*=.73f; if(edge>.102f&&edge<.11f)shade*=1.07f;}
                if(tile==2)shade=.8f+.2f*v;
                Color c=color*shade;c.a=1;pixels[((tile/4)*n+y)*n*4+(tile%4)*n+x]=c;
            }
        }
        tex.SetPixels(pixels);tex.Apply();string path=Folder+"/doll_bear2_robot_albedo.png";File.WriteAllBytes(path,tex.EncodeToPNG());UnityEngine.Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path);var importer=(TextureImporter)AssetImporter.GetAtPath(path);importer.mipmapEnabled=true;importer.wrapMode=TextureWrapMode.Clamp;importer.maxTextureSize=1024;importer.textureCompression=TextureImporterCompression.CompressedHQ;importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }
    [MenuItem("Tools/Robot Bear/Inspect source")]
    public static void Inspect()
    {
        var root = PrefabUtility.LoadPrefabContents(Source);
        try
        {
            var log = new StringBuilder();
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (!t.name.StartsWith("Hull")) log.AppendLine($"{t.name} parent={t.parent?.name} pos={root.transform.InverseTransformPoint(t.position):F4} local={t.localPosition:F4} rot={t.localEulerAngles:F1} scale={t.lossyScale:F3} active={t.gameObject.activeSelf}");
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                Mesh mesh = r is SkinnedMeshRenderer s ? s.sharedMesh : r.GetComponent<MeshFilter>()?.sharedMesh;
                log.AppendLine($"RENDER {r.name} enabled={r.enabled} mesh={mesh?.name} bounds={mesh?.bounds} material={AssetDatabase.GetAssetPath(r.sharedMaterial)}");
                if (!(r is SkinnedMeshRenderer skin)) continue;
                var verts = mesh.vertices; var weights = mesh.boneWeights;
                for (int b = 0; b < skin.bones.Length; b++)
                {
                    var points = Enumerable.Range(0, verts.Length).Where(i => weights[i].boneIndex0 == b).Select(i => root.transform.InverseTransformPoint(r.transform.TransformPoint(verts[i]))).ToArray();
                    if (points.Length == 0) continue;
                    Bounds bounds = new Bounds(points[0], Vector3.zero); foreach (var p in points) bounds.Encapsulate(p);
                    log.AppendLine($"BONE {b}: {skin.bones[b].name} dominantBounds={bounds.ToString("F4")} bind={mesh.bindposes[b]}");
                }
            }
            Directory.CreateDirectory(".utmp"); File.WriteAllText(".utmp/robot_bear_source.txt", log.ToString());
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }
}
