using System;
using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using Random = UnityEngine.Random;

public class Node {
    public Node(Rect bounds) {
        this.bounds = bounds;
    }

    public Node(Vector2 center, Vector2 size) {
        bounds = new Rect(center, size);
    }

    public Rect bounds;
    public List<Node> nodes = new();
    public List<Node> leaves = new();
}

public struct MinMaxData {
    public float min, max;
    public Node minRef, maxRef;
}

public class Rtree : MonoBehaviour
{
    private const int M = 5;
    private const int WORLD_SIZE = 30;

    public Node root;
    public int step = 0;
    public List<Action> steps;
    public List<Color> colors = new() { Color.white };
    public bool debug;

    private void OnDrawGizmos() {
        if(!debug || root == null) return;

        DrawNode(root, 0, false);
    }

    private void DrawNode(Node node, int colorIndex, bool isNode) {
        colorIndex %= colors.Count;

        Gizmos.color = colors[colorIndex];
        if (isNode) {
            Gizmos.DrawCube(node.bounds.center, node.bounds.size);
        } else {
            Gizmos.DrawWireCube(node.bounds.center, node.bounds.size);
        }

        if (node.leaves.Count > 0) {
            foreach(Node leaf in node.leaves) {
                DrawNode(leaf, colorIndex + 1, false);
            }
        }

        foreach(Node child in node.nodes) {
            DrawNode(child, colorIndex + 1, true);
        }
    }

    private void Awake() {
        root = new Node(new Vector2(-WORLD_SIZE / 2f, -WORLD_SIZE / 2f), new Vector2(WORLD_SIZE, WORLD_SIZE));

        steps = new List<Action>() {
             Noop,
             //Simulate,
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

    private void Update() {
        steps[step].Invoke();                
    }

    public void NextStep() {
        step++;
        if (step >= steps.Count) {
            step = steps.Count - 1;
        } else {
            Debug.Log($"Next step! {step}");
        }
    }

    public void AddNode(Node parent, Node node) {
        //check if parent is leaf
        if(parent.leaves.Count == 0) {
            // parent is leaf, try to add node
            parent.nodes.Add(node);

            //expand leaf to accomodate new node
            parent.bounds = Expand(parent.bounds, node.bounds);

            //check capacity
            if (parent.nodes.Count >= M) {
                //parent is full, need partititon
                var minMaxData = GetMinMax(ref parent);

                //create 2 leafs, to add furthest nodes apart
                Node leafA = new(minMaxData.minRef.bounds);
                leafA.nodes.Add(minMaxData.minRef);

                Node leafB = new(minMaxData.maxRef.bounds);
                leafB.nodes.Add(minMaxData.maxRef);

                //remove nodes from parent
                parent.nodes.Remove(minMaxData.minRef);
                parent.nodes.Remove(minMaxData.maxRef);

                //for the second iteration, we're going to check
                //for area increase and overlap
                var minMaxX = GetMinMaxX(parent);
                var minMaxY = GetMinMaxY(parent);

                //X Case
                Rect leafAXCase = Expand(leafA.bounds, minMaxX.minRef.bounds);
                Rect leafBXCase = Expand(leafB.bounds, minMaxX.maxRef.bounds);
                float totalXCaseArea = (leafAXCase.width * leafAXCase.height) + (leafBXCase.width * leafBXCase.height);
                //Debug.Log($"XCase area: {totalXCaseArea}");

                //Y Case
                Rect leafAYCase = Expand(leafA.bounds, minMaxY.minRef.bounds);
                Rect leafBYCase = Expand(leafB.bounds, minMaxY.maxRef.bounds);
                float totalYCaseArea = (leafAYCase.width * leafAYCase.height) + (leafBYCase.width * leafBYCase.height);
                //Debug.Log($"YCase area: {totalYCaseArea}");

                //compare cases by area
                int xScore = 0;
                int yScore = 0;
                if(totalXCaseArea < totalYCaseArea) {
                    xScore++;
                    //Debug.Log($"XCase Selected for area!");
                }
                else if (totalXCaseArea > totalYCaseArea ) {
                    yScore++;
                    //Debug.Log($"YCase Selected for area!");
                } else {
                    //Debug.Log($"Area draw!");
                }

                // check overlaps
                if (!leafAXCase.Overlaps(leafBXCase)) {
                    xScore++;
                    //Debug.Log($"XCase no overlap!");
                }

                if (!leafAYCase.Overlaps(leafBYCase)) {
                    yScore++;
                    //Debug.Log($"YCase no overlap!");
                }

                bool xPriority = true;
                bool xSelected = xPriority;
                //Debug.Log($"XScore: {xScore}");
                //Debug.Log($"YScore: {yScore}");
                //take final decision
                if (xScore > yScore) {
                    //Debug.Log($"XCase selected!");
                    xSelected = true;
                } else if (yScore > xScore) {
                    //Debug.Log($"YCase selected!");
                    xSelected = false;
                } else {
                    //Debug.Log($"Draw! (selected by priority flag)");
                }

                Node minRef = xSelected ? minMaxX.minRef : minMaxY.minRef;
                Node maxRef = xSelected ? minMaxX.maxRef : minMaxY.maxRef;
                Rect leafABounds = xSelected ? leafAXCase : leafAYCase;
                Rect leafBBounds = xSelected ? leafBXCase : leafBYCase;

                //add closest ones to respective leafs
                leafA.nodes.Add(minRef);
                //expand leaf to encompass 
                leafA.bounds = leafABounds;

                //do same to other node
                leafB.nodes.Add(maxRef);
                //expand leaf to encompass 
                leafB.bounds = leafBBounds;

                //remove nodes from parent
                parent.nodes.Remove(minRef);
                parent.nodes.Remove(maxRef);

                //for last node, check if any leaf already encompass it
                if (leafA.bounds.Overlaps(parent.nodes[0].bounds)) {
                    leafA.nodes.Add(parent.nodes[0]);
                    leafA.bounds = Expand(leafA.bounds, parent.nodes[0].bounds);
                    //remove from parent
                    parent.nodes.Clear();
                } else if (leafB.bounds.Overlaps(parent.nodes[0].bounds)) {
                    leafB.nodes.Add(parent.nodes[0]);
                    leafB.bounds = Expand(leafB.bounds, parent.nodes[0].bounds);
                    //remove from parent
                    parent.nodes.Clear();
                } else {
                    //no leafs overlap, get closest by distance
                    var center = parent.nodes[0].bounds.center;
                    if (Vector2.Distance(leafA.bounds.center, center) < Vector2.Distance(leafB.bounds.center, center)) {
                        leafA.nodes.Add(parent.nodes[0]);
                        leafA.bounds = Expand(leafA.bounds, parent.nodes[0].bounds);
                        //remove from parent
                        parent.nodes.Clear();
                    } else {
                        leafB.nodes.Add(parent.nodes[0]);
                        leafB.bounds = Expand(leafB.bounds, parent.nodes[0].bounds);
                        //remove from parent
                        parent.nodes.Clear();
                    }
                }
                parent.leaves.Add(leafA);
                parent.leaves.Add(leafB);
            }
            else {
                //node added with no consequences
                return;
            }
        }
        else {
            // current node is not a leaf
            // start by checking if we overlap any of the leaves
            foreach (var leaf in parent.leaves) {
                if(leaf.bounds.Overlaps(node.bounds)) {
                    AddNode(leaf, node);
                    parent.bounds = Expand(parent.bounds, leaf.bounds);
                    return;
                }               
            }

            // if no leaf is overlaping, choose by distance
            float minDist = float.PositiveInfinity;
            float d;
            Node minRef = null;
            foreach (var leaf in parent.leaves) {
                d = Vector2.Distance(leaf.bounds.center, node.bounds.center);
                if (d < minDist) {
                    minDist = d;
                    minRef = leaf;
                }
            }

            if(minRef == null) {
                Debug.Log("WHAT T F HAPPENED?");
                return;
            }

            AddNode(minRef, node);
            parent.bounds = Expand(parent.bounds, minRef.bounds);
            return;
        }
    }

    public MinMaxData GetMinMaxX(Node parent) {
        MinMaxData minMaxData = new();
        //set def values
        float minX = float.PositiveInfinity;
        float maxX = float.NegativeInfinity;
        Node minXref = null;
        Node maxXref = null;

        foreach (Node node in parent.nodes) {
            if (node.bounds.xMin < minX) {
                minX = node.bounds.xMin;
                minXref = node;
            }

            if (node.bounds.xMax > maxX) {
                maxX = node.bounds.xMax;
                maxXref = node;
            }
        }

        minMaxData.min = minX;
        minMaxData.max = maxX;
        minMaxData.minRef = minXref;
        minMaxData.maxRef = maxXref;

        return minMaxData;
    }

    public MinMaxData GetMinMaxY(Node parent) {
        MinMaxData minMaxData = new();
        //set def values
        float minY = float.PositiveInfinity;
        float maxY = float.NegativeInfinity;
        Node minYref = null;
        Node maxYref = null;

        foreach (Node node in parent.nodes) {
            if (node.bounds.yMin < minY) {
                minY = node.bounds.yMin;
                minYref = node;
            }

            if (node.bounds.yMax > maxY) {
                maxY = node.bounds.yMax;
                maxYref = node;
            }
        }

        minMaxData.min = minY;
        minMaxData.max = maxY;
        minMaxData.minRef = minYref;
        minMaxData.maxRef = maxYref;

        return minMaxData;
    }

    public MinMaxData GetMinMax(ref Node parent) {
        MinMaxData minMaxData = new();
        //set def values
        float minX = float.PositiveInfinity;
        float minY = float.PositiveInfinity;
        float maxX = float.NegativeInfinity;
        float maxY = float.NegativeInfinity;
        Node minXref = null;
        Node maxXref = null;
        Node minYref = null;
        Node maxYref = null;

        //iterate saving min/max x/y with ref to owners
        foreach(Node node in parent.nodes) { 
            if(node.bounds.xMin < minX) {
                minX = node.bounds.xMin;
                minXref = node;
            }

            if (node.bounds.xMax > maxX) {
                maxX = node.bounds.xMax;
                maxXref = node;
            }

            if (node.bounds.yMin < minY) {
                minY = node.bounds.yMin;
                minYref = node;
            }

            if (node.bounds.yMax > maxY) {
                maxY = node.bounds.yMax;
                maxYref = node;
            }
        }

        if (maxX - minX > maxY - minY) {
            minMaxData.min = minX;
            minMaxData.max = maxX;
            minMaxData.minRef = minXref;
            minMaxData.maxRef = maxXref;
        }
        else
        {
            minMaxData.min = minY;
            minMaxData.max = maxY;
            minMaxData.minRef = minYref;
            minMaxData.maxRef = maxYref;
        }

        return minMaxData;
    }

    public Rect Expand(Rect leafBounds, Rect nodeBounds) {
        leafBounds.xMin = Mathf.Min(leafBounds.xMin, nodeBounds.xMin);
        leafBounds.yMin = Mathf.Min(leafBounds.yMin, nodeBounds.yMin);
        leafBounds.xMax = Mathf.Max(leafBounds.xMax, nodeBounds.xMax);
        leafBounds.yMax = Mathf.Max(leafBounds.yMax, nodeBounds.yMax);

        return leafBounds;
    }

    private void Simulate() {
        //int iterations = 100;

        //for (int i = 0; i < iterations; i++) {
            var x = Random.Range(-WORLD_SIZE / 2f + 1f, WORLD_SIZE / 2f - 5f);
            var y = Random.Range(-WORLD_SIZE / 2f + 1f, WORLD_SIZE / 2f - 5f);
            var w = Random.Range(0.25f, 1f);
            Node newObj = new(new Vector2(x, y), new Vector2(w, w));
            AddNode(root, newObj);
        //}

        step = 0;
    }

    private void Noop() {
        if (Input.GetKeyDown(KeyCode.Space)) {
            NextStep();
        }
    }

    private void Step0() {
        //fill root
        Node obj0 = new(new Vector2(1f, -3f), new Vector2(2f, 2f));
        AddNode(root, obj0);
        Node obj1 = new (new Vector2(-3f, -2f), new Vector2(1f, 1f));
        AddNode(root, obj1);
        Node obj2 = new (new Vector2(-4f, 0f), new Vector2(2f, 2f));
        AddNode(root, obj2);
        Node obj3 = new (new Vector2(2f, 0f), new Vector2(2f, 2f));
        AddNode(root, obj3);
        NextStep();
    }

    private void Step1() {
        //overflow root, cause patition
        Node obj4 = new (new Vector2(-1f, 2f), new Vector2(1f, 1f));
        AddNode(root, obj4);
        NextStep();
    }

    private void Step2() {
        //new node is inside leaf
        Node obj5 = new(new Vector2(-1f, -1f), new Vector2(1f, 1f));
        AddNode(root, obj5);
        //new node made leaf expand
        Node obj6 = new(new Vector2(2f, 3f), new Vector2(1f, 1f));
        AddNode(root, obj6);
        NextStep();
    }

    private void Step3() {
        //expand and cause new partition
        Node obj7 = new(new Vector2(-3f, -4f), new Vector2(1f, 1f));
        AddNode(root, obj7);
        NextStep();
    }
}
