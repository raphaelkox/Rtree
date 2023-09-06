using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using Random = UnityEngine.Random;

public class TestData : IBoundsProvider, IDataContainer<float>, IEquatable<TestData> {
    public float ObjectData {get; private set;}

    private Rect bounds;

    public TestData(float objectData, Rect bounds) {
        this.bounds = bounds;
        this.ObjectData = objectData;
    }

    public bool Equals(TestData other) {
        return ObjectData == other.ObjectData;
    }

    public Rect GetBounds() {
        return bounds;
    }

    public void SetData(float data) {
        ObjectData = data;
    }
}

public class RTreeTest : MonoBehaviour
{
    [SerializeField] private float worldSize = 10f;
    [SerializeField] private int maxObjects = 50;
    [SerializeField] private float minNodeSize = 1f;
    [SerializeField] private float maxNodeSize = 4f;
    [SerializeField] private float interactSpawnAmmount = 10;
    [SerializeField] private BoxCollider2D queryArea;
    [SerializeField] private List<Color> colors = new() { Color.white };
    [SerializeField] private bool drawNodes;
    [SerializeField] private bool drawContainers;
    [SerializeField] private bool interactive;

    private Rtree<TestData> rTree;

    private int step = 0;
    private List<Action> steps;

    private void OnDrawGizmos() {
        if (rTree == null || rTree.Root == null) return;

        DrawNode(rTree.Root, 0, false);
    }

    private void DrawNode(IRTreeNode node, int colorIndex, bool isNode) {
        colorIndex %= colors.Count;

        Gizmos.color = colors[colorIndex];
        if (isNode) {
            if(drawNodes) Gizmos.DrawCube(node.Bounds.center, node.Bounds.size);
        } else {
            if (drawContainers) Gizmos.DrawWireCube(node.Bounds.center, node.Bounds.size);
        }

        if (node.Leaves.Count > 0) {
            foreach (IRTreeNode leaf in node.Leaves) {
                DrawNode(leaf, colorIndex + 1, false);
            }
        }

        foreach (IRTreeNode child in node.Children) {
            DrawNode(child, colorIndex + 1, true);
        }
    }

    private void Awake() {
        rTree = new Rtree<TestData>(worldSize, maxObjects);

        if (interactive) {
            steps = new List<Action>() {
             Noop,
             SimulateInteractive
            };
        }
        else {
            steps = new List<Action>() {
             Noop,
             Step0,
             Noop,
             Step1,
             Noop,
             Step2,
             Noop,
             Step3,
             Noop,
         };
        }        
    }

    private void Update() {
        steps[step].Invoke();
    }

    public void NextStep() {
        step++;
        if (step >= steps.Count) {
            step = steps.Count - 1;
        }
    }

    private int currentIndex = 0;
    private void SimulateInteractive() {
        for (int i = 0; i < interactSpawnAmmount; i++) {
            var x = Random.Range(-worldSize / 2f + 1f, worldSize / 2f - maxNodeSize - 1f);
            var y = Random.Range(-worldSize / 2f + 1f, worldSize / 2f - maxNodeSize - 1f);
            var w = Random.Range(minNodeSize, maxNodeSize);
            TestData newObj = new(currentIndex, new Rect(new Vector2(x, y), new Vector2(w, w)));
            rTree.AddObject(newObj);
            currentIndex++;
        }

        step = 0;
    }

    private void QueryInteractive() {
        var region = new Rect(queryArea.bounds.min.x, queryArea.bounds.min.y, queryArea.bounds.size.x, queryArea.bounds.size.y);
        List<TestData> result = new();

        rTree.QueryObjectsNonRecursive(region, result);
        
        foreach (var obj in result) {
            Debug.Log($"{obj.ObjectData}");
        }
    }

    private void Simulate() {
        int iterations = 10000;

        for (int i = 0; i < iterations; i++) {
            var x = Random.Range(-worldSize / 2f + 1f, worldSize / 2f - 5f);
            var y = Random.Range(-worldSize / 2f + 1f, worldSize / 2f - 5f);
            var w = Random.Range(0.25f, 1f);
            TestData newObj = new(0f, new Rect(new Vector2(x, y), new Vector2(w, w)));
            rTree.AddObject(newObj);
        }

        NextStep();
    }

    private void Noop() {
        if (Input.GetKeyDown(KeyCode.Space) || Input.GetMouseButtonDown(0)) {
            NextStep();
        }

        if (Input.GetMouseButtonDown(1)) {
            QueryInteractive();
        }
    }

    private void Step0() {
        //fill root
        TestData obj0 = new TestData(0f, new Rect(new Vector2(1f, -3f), new Vector2(2f, 2f)));
        rTree.AddObject(obj0);
        TestData obj1 = new TestData(1f, new Rect(new Vector2(-3f, -2f), new Vector2(1f, 1f)));
        rTree.AddObject(obj1);
        TestData obj2 = new TestData(2f, new Rect(new Vector2(-4f, 0f), new Vector2(2f, 2f)));
        rTree.AddObject(obj2);
        TestData obj3 = new TestData(3f, new Rect(new Vector2(2f, 0f), new Vector2(2f, 2f)));
        rTree.AddObject(obj3);
        NextStep();
    }

    private void Step1() {
        //overflow root, cause patition
        TestData obj4 = new TestData(4f, new Rect(new Vector2(-1f, 2f), new Vector2(1f, 1f)));
        rTree.AddObject(obj4);

        NextStep();
    }

    private void Step2() {
        //new node is inside leaf
        TestData obj5 = new TestData(5f, new Rect(new Vector2(-1f, -1f), new Vector2(1f, 1f)));
        rTree.AddObject(obj5);
        TestData obj6 = new TestData(6f, new Rect(new Vector2(2f, 3f), new Vector2(1f, 1f)));
        rTree.AddObject(obj6);
        NextStep();
    }

    private void Step3() {
        //expand and cause new partition
        TestData obj7 = new TestData(7f, new Rect(new Vector2(-3f, -4f), new Vector2(1f, 1f)));
        rTree.AddObject(obj7);
        NextStep();
    }
}
