using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

    /// <summary>
    /// Controls the mesh generation and the positions of Snake's tail
    /// </summary>

// todo general
// => solve the vague wonkiness (performance?) -> seems like it is
// => generate mesh and bones every time GrowSnake() is called instead of
//      generating the mesh 50 times per frame
//      [https://docs.unity3d.com/ScriptReference/Mesh.SetBoneWeights.html]
// => add rigidbodies for each bone and physics?

// maybe
//  reform the OrientedPoint struct to contain bones and weigths

// todo tasks
//  in GenerateMesh()
//      - create bones at localOrigins and set the weights
//      - bind Mesh to the SkinnedMeshRenderer component


[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(SkinnedMeshRenderer))]
public class SnakeBody : MonoBehaviour
{
    [Header("Mesh Generation")]
    [SerializeField] MeshSlice shape;
    [SerializeField] bool preview = true;
    [SerializeField] bool debug = true;
    [SerializeField] bool ShowBezier = true;
    [SerializeField] [Range(.01f, 1f)] float bodyThickness = .5f;
    [SerializeField] Transform[] thicknessModulator = new Transform[4];
    [SerializeField] int initialSegmentsCount = 10;
    [SerializeField] int maxSegmentsCount = 50;
    [SerializeField] float segmentsInterval = .5f;

    [Space]

    [Header("Initial Pose")] // * might not be needed anymore
    [SerializeField] Transform[] initialPoseControlPoints = new Transform[4];

    [Space]

    [Header("Movement")]
    [SerializeField] Transform head;
    [SerializeField] float movementDamping = .08f;
    [SerializeField] float trailResponse = 200f;

    [SerializeField] LineRenderer linePreview;

//  Mesh generation internal data
    private OrientedPoint[] segmentPoints;
    // NOTE OrientedPoint struct contains
    //      position, rotation and velocity

    private BoneWeight[] arr_weights; // list is created in GenerateBodyMesh()
    private Transform[] bones;
    private float[] thicknessMapping; // could add that to OrientedPoint :thinking:
    private Mesh mesh;
    private int vc;
    private ushort step;
    private ushort stop;
    private int currentSegmentsCount;

    void Awake()
    {
        vc = shape.VertCount;
        currentSegmentsCount = initialSegmentsCount;

        segmentPoints = new OrientedPoint[maxSegmentsCount];
        thicknessMapping = new float[maxSegmentsCount];
        bones = new Transform[maxSegmentsCount];
        for (int i = 0; i < bones.Length; i++)
        {
            bones[i] = new GameObject("Spine").transform;
        }

        if (shape.isSmooth)
        {
            step = 1;
            stop = 1;
        }
        else
        {
            step = 2;
            stop = 0;
        }

        if (shape.isSymmetrical)
        {
            for (int i = 0; i < vc; i++)
            {
                shape.baseVertices[i].c = (shape.baseVertices[i].point.y + 1) / 2f;
            }
        }

        mesh = new Mesh();
        mesh.name = "Snake Body";
        GetComponent<SkinnedMeshRenderer>().sharedMesh = mesh;
        GetComponent<SkinnedMeshRenderer>().bones = bones;

        if(debug) linePreview.positionCount = initialSegmentsCount;

        PopulateInitialPositions(false);

        if(debug) {
            for (int i = 0; i < currentSegmentsCount; i++)
            {
                linePreview.SetPosition(i, segmentPoints[i].position);
            }
        }

        GenerateBodyMesh();

    }

    void OnDrawGizmos()
    {
        if(ShowBezier)
        {
            BezierUtils.DrawBezierCurve(thicknessModulator);
            BezierUtils.DrawBezierCurve(initialPoseControlPoints);
        }
        if(preview) DrawBodyPreview();
        
    }

    void Update()
    {
        // this is equivalent to the old PopulateInitialPositions()
        segmentPoints[0].position = head.position;
        segmentPoints[0].rotation = head.rotation;

        if(debug) linePreview.SetPosition(0, segmentPoints[0].position);

        for (int i = 1; i < currentSegmentsCount; i++)
        {
            Vector3 target = segmentPoints[i-1].position;
            Vector3 current = segmentPoints[i].position;
            Vector3 bufferDist = -head.forward * segmentsInterval;
            Vector3 dir = target - current;

            segmentPoints[i].position = Vector3.SmoothDamp(
                current,
                target + bufferDist,
                ref segmentPoints[i].velocity,
                movementDamping + i / trailResponse);
            
            segmentPoints[i].rotation = Quaternion.LookRotation(dir);

            if(debug) linePreview.SetPosition(i, segmentPoints[i].position);

        }
        // GenerateBodyMesh();
        // calling this upon every frame causes wonkiness
        // gizmos are capable of updading correctly but generating a mesh
        // every frame or at each step of the for loop is kinda bad
        // not enough to cause an actual gameplay issue but enough for it to look bad

    }


    public void GrowSnake() {
        if (currentSegmentsCount < maxSegmentsCount)
        {
            Debug.Log("Grow the snake");
            linePreview.positionCount++; // * testing only
            currentSegmentsCount++;
            // GenerateBodyMesh();
        }
        else Debug.Log("Snake has attained its maximum length.");
        
    }
    private void GenerateBodyMesh()
    {
        //Debug.Log("Generating body mesh");

        mesh.Clear();
        //Debug.Log("Mesh Cleared");

        // vertices data
        List<Vector3> inVertices = new List<Vector3>();
        List<Vector3> inNormals = new List<Vector3>();
        List<Vector2> uvs = new List<Vector2>();
        List<int> triangles = new List<int>();
        List<BoneWeight> weights = new List<BoneWeight>();
        //Debug.Log("Lists of vertex information created");

        // populate vertices

        for (int slice = 0; slice < currentSegmentsCount; slice++)
        {
            OrientedPoint localOrigin = segmentPoints[slice];
            //Debug.Log($"position at slice {slice}: {localOrigin.position}");

            float t = slice / (currentSegmentsCount - 1f);
            float m = BezierUtils.CalculateBezierPoint(t, thicknessModulator).position.x;
            thicknessMapping[slice] = m;
            //Debug.Log($"thickness modulator = {m}");

            // assigning bones positions to the local origin point of the mesh
            bones[slice].position = localOrigin.position;
        

            for (int i = 0; i < shape.VertCount; i++)
            {

                // assign position data
                inVertices.Add(
                    head.InverseTransformPoint(
                        localOrigin.GetDisplacedPoint(
                            shape.baseVertices[i].point*bodyThickness*m)));
                //Debug.Log("added vertices");

                // assign normals data
                if (shape.isSmooth) {
                    // if the shape is smooth the orientation of the normal is the same as the point
                    inNormals.Add(localOrigin.GetOrientationPoint(shape.baseVertices[i].point));
                } else {
                    // otherwise rely on data input
                    inNormals.Add(localOrigin.GetOrientationPoint(shape.baseVertices[i].normal));
                }
                //Debug.Log("added normals");

                // assign UV data
                uvs.Add(new Vector2(t * currentSegmentsCount / 2, shape.baseVertices[i].c));
                
                // assign weight data
                BoneWeight currentWeight = new BoneWeight();
                currentWeight.boneIndex0 = slice;
                currentWeight.weight0 = 1;
                weights.Add(currentWeight);

            }
        }

        // read vertices to draw triangles
        // loop in slices
        for (int s = 0; s < currentSegmentsCount - 1; s++)
        {
            // vc for vertex count
            int root = s * vc ;
            int rootNext = (s + 1) * vc;

            //Debug.Log(root);
            //Debug.Log(rootNext);

            // loop in mesh vertices
            // this will not work correctly with split vertices for hard edges
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

        mesh.SetVertices(inVertices);
        mesh.SetNormals(inNormals);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        
        arr_weights = new BoneWeight[mesh.vertices.Length];
        for (int i = 0; i < weights.Count; i++)
        {
            arr_weights[i] = weights[i];
        }

        bones[0].parent = transform;
        for (int i = 1; i < bones.Length; i++)
        {
            bones[i].parent = bones[i-1];
        }

        mesh.boneWeights = arr_weights;
    }

    private void PopulateInitialPositions(bool global=true)
    {
        //Debug.Log("Populating initial positions");
        for (int i = 0; i < currentSegmentsCount; i++)
        {
            OrientedPoint localOrigin;
            float t = i / (currentSegmentsCount - 1f);

            if(global) localOrigin = BezierUtils.CalculateBezierPoint(t, initialPoseControlPoints);
            else localOrigin = BezierUtils.CalculateBezierPoint(t, initialPoseControlPoints, false);

            segmentPoints[i] = localOrigin;
            //Debug.Log($"position at index {i} : {positionsHistory[i].position}");

        }

        for (int i = 0; i < currentSegmentsCount; i++)
        {
            float t = i / (currentSegmentsCount - 1f);
            float m = BezierUtils.CalculateBezierPoint(t, thicknessModulator).position.x;
            thicknessMapping[i] = m;

        }
    }

    private void DrawBodyPreview()
    {
        //Debug.Log("Draw body preview");

        Gizmos.color = Color.white;
        for (int i = 0; i < currentSegmentsCount; i ++)
        {
            Gizmos.DrawSphere(segmentPoints[i].position, .02f);
            OrientedPoint origin = segmentPoints[i];
            float m = thicknessMapping[i];

            for (int v = 0; v < shape.baseVertices.Length - 1; v++)
            {
                Vector3 a = origin.GetDisplacedPoint(shape.baseVertices[v].point * bodyThickness * m);
                Vector3 b = origin.GetDisplacedPoint(shape.baseVertices[v + 1].point * bodyThickness * m);
                Gizmos.DrawLine(a, b);
            }
        }
    }

}
