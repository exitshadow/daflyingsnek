using System.Runtime.Serialization;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// todo
//  adapt snake body to better rigging logic
//  dump bezier things except for the thickness curve
//  dump PopulateInitialPositions()
//  solve wonky parenting issue

[ExecuteInEditMode]
[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(SkinnedMeshRenderer))]
public class RiggedBody : MonoBehaviour
{
    [Header("Mesh Generation")]
    [SerializeField] private MeshSlice shape;
    [SerializeField] private float thickness = .5f;
    [SerializeField] private Transform[] thicknessCurve = new Transform[4];
    [SerializeField] private int initialSegmentsCount = 10;
    [SerializeField] private int maxSegmentsCount = 50;
    [SerializeField] private float segmentsInterval = .5f;

    [Space]
    [Header("Rigging Animation")]
    [SerializeField] Transform head;
    [SerializeField] private float movementDamping = .08f;
    [SerializeField] private float trailResponse = 200f;

    [Space]
    [Header("Debugging")]    
    [SerializeField] private bool debug = true;
    [SerializeField] private LineRenderer linePreview;

    // mesh gen internal data
    private SkinnedMeshRenderer skin;
    private Mesh mesh;
    private int currentSegmentsCount;
    private float[] thicknessMapping;

    // rigging gen internal data
    private Transform[] bones;
    private Matrix4x4[] bindPoses;
    private BoneWeight[] weights;

    private void Start()
    {
        // rig setup
        bones = new Transform[maxSegmentsCount];
        bindPoses = new Matrix4x4[maxSegmentsCount];
            for (int i = 0; i < bones.Length; i++)
            {
                bones[i] = new GameObject($"Spine_{i}").transform;

                if(i == 0) bones[0].parent = transform;
                else bones[i].parent = bones[i-1];

                bones[i].localPosition = new Vector3(0, 0, segmentsInterval);
                bones[i].localRotation = Quaternion.identity;
                
                bindPoses[i] = bones[i].worldToLocalMatrix * transform.localToWorldMatrix;
            }
            


        // mesh setup
        skin = GetComponent<SkinnedMeshRenderer>();
        mesh = new Mesh();
        mesh.name = "Snake Body";
        skin.sharedMesh = mesh;
        skin.bones = bones;

        // intialization
        currentSegmentsCount = initialSegmentsCount;
        GenerateBodyMesh();
    }

    private void GenerateBodyMesh()
    {
        // shape management
        int vc = shape.VertCount;
        int step, stop;

        if (shape.isSmooth) {
            step = 1;
            stop = 1;
        } else {
            step = 2;
            stop = 0;
        }

        if (shape.isSymmetrical)
        {
            for (int i = 0; i < vc; i++)
            {
                // mapping the Y height of the mesh to the V position on the uv
                shape.baseVertices[i].c = (shape.baseVertices[i].point.y + 1) / 2f;
            }
        }

        // starting generation
        Debug.Log("Generating body mesh...");
        mesh.Clear();

        // vertices buffer data
        List<Vector3> vertices = new List<Vector3>();
        List<Vector3> normals = new List<Vector3>();
        List<Vector2> uvs = new List<Vector2>();
        List<int> triangles = new List<int>();
        List<BoneWeight> boneWeights = new List<BoneWeight>();
        Vector3[] origins = new Vector3[currentSegmentsCount];
        

        // populate vertices

        // looping through each slice
        for (int slice = 0; slice < currentSegmentsCount; slice++)
        {
            // mapping thickness curve to points in slice
            float t = slice / (currentSegmentsCount - 1f);
            float m = BezierUtils.CalculateBezierPoint(t, thicknessCurve).position.x;
            thicknessMapping[slice] = m;

            // use the current bone as the origin for drawing a slice
            Transform origin = bones[slice];

            // looping in the vertices around each slice for extrusion
            for (int i = 0; i < vc; i++)
            {
                Vector3 point = shape.baseVertices[i].point;
                Vector3 normal = shape.baseVertices[i].normal;
                float v = shape.baseVertices[i].c;

                Vector3 pos = point * thickness * m; // relative position on the slice
                Vector3 vertex = origin.localPosition + origin.localRotation * pos; // mapped to current origin

                // assign position
                vertices.Add(transform.InverseTransformPoint(vertex));

                // assign normal
                if (shape.isSmooth) {
                    normals.Add(origin.rotation * point);
                } else normals.Add(origin.rotation * normal);
                    // the normal of a smooth point is the same as its unique vertex
                    // while hard edges do have split vertices

                // assign uv
                uvs.Add(new Vector2(t * currentSegmentsCount / 2, v));

                // assign weights
                BoneWeight weight = new BoneWeight();
                weight.boneIndex0 = slice;
                weight.weight0 = 1;
                boneWeights.Add(weight);
            }
        }

        // adding triangle indices
        // loop in slices
        for (int s = 0; s < currentSegmentsCount - 1; s++)
        {
            int root = s * vc ;
            int rootNext = (s + 1) * vc;

            //Debug.Log(root);
            //Debug.Log(rootNext);

            // loop in mesh vertices
            for (int v = 0; v < vc - stop ; v+= step)
            {
                int node_a = shape.edgeLinksNodes[v];
                int node_b = shape.edgeLinksNodes[v+1];

                int a = root + node_a;
                int b = root + node_b;
                int ap = rootNext + node_a;
                int bp = rootNext + node_b;

                triangles.Add(a);
                triangles.Add(ap);
                triangles.Add(b);
                //Debug.Log($"Face{a}, Tri01: {a}, {ap}, {b}");

                triangles.Add(b);
                triangles.Add(ap);
                triangles.Add(bp);
                //Debug.Log($"Face{b}, Tri02: {b}, {ap}, {bp}");
            }
        }

        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);

        weights = new BoneWeight[currentSegmentsCount * vc];
            for (int i = 0; i < boneWeights.Count; i++) weights[i] = boneWeights[i];
        
        mesh.boneWeights = weights;
        skin.sharedMesh = mesh;

    }


}