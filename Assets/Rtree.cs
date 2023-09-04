using System;
using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;

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

    public void Expand(Node node) {
        bounds.xMin = Mathf.Min(bounds.xMin, node.bounds.xMin);
        bounds.yMin = Mathf.Min(bounds.yMin, node.bounds.yMin);
        bounds.xMax = Mathf.Max(bounds.xMax, node.bounds.xMax);
        bounds.yMax = Mathf.Max(bounds.yMax, node.bounds.yMax);
    }
}

public struct MinMaxData {
    public float min, max;
    public Node minRef, maxRef;
}

public class Rtree : MonoBehaviour
{
    private const int M = 5;

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
        root = new Node(new Vector2(-5, -5f), new Vector2(10, 10));

        steps = new List<Action>() {
             Noop,
             Step0,            
             Noop,
             Step1,
             Noop
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

            //check capacity
            if(parent.nodes.Count >= M) {
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

                //repeat for remaining 3 nodes
                minMaxData = GetMinMax(ref parent);

                //add closest ones to respective leafs
                leafA.nodes.Add(minMaxData.minRef);
                //expand leaf to encompass 
                leafA.Expand(minMaxData.minRef);

                //do same to other node
                leafB.nodes.Add(minMaxData.maxRef);
                //expand leaf to encompass 
                leafB.Expand(minMaxData.maxRef);

                //remove nodes from parent
                parent.nodes.Remove(minMaxData.minRef);
                parent.nodes.Remove(minMaxData.maxRef);

                //for last node, check if any leaf already encompass it
                if (leafA.bounds.Overlaps(parent.nodes[0].bounds)) {
                    leafA.nodes.Add(parent.nodes[0]);
                    leafA.Expand(parent.nodes[0]);
                    //remove from parent
                    parent.nodes.Clear();
                } else if (leafB.bounds.Overlaps(parent.nodes[0].bounds)) {
                    leafB.nodes.Add(parent.nodes[0]);
                    leafB.Expand(parent.nodes[0]);
                    //remove from parent
                    parent.nodes.Clear();
                } else {
                    //no leafs overlap, get closest by distance
                    var center = parent.nodes[0].bounds.center;
                    if (Vector2.Distance(leafA.bounds.center, center) < Vector2.Distance(leafB.bounds.center, center)) {
                        leafA.nodes.Add(parent.nodes[0]);
                        leafA.Expand(parent.nodes[0]);
                        //remove from parent
                        parent.nodes.Clear();
                    } else {
                        leafB.nodes.Add(parent.nodes[0]);
                        leafB.Expand(parent.nodes[0]);
                        //remove from parent
                        parent.nodes.Clear();
                    }
                }
                parent.leaves.Add(leafA);
                parent.leaves.Add(leafB);
            }
            else {
                //parent is good, lest go back
                return;
            }
        }
        else {
            // current node is not a leaf
            // start by checking if we overlap any of the leaves
            foreach (var leaf in parent.leaves) {
                if(leaf.bounds.Overlaps(node.bounds)) {
                    AddNode(leaf, node);
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
            if (node.bounds.xMin < minY) {
                minY = node.bounds.xMin;
                minYref = node;
            }

            if (node.bounds.xMax > maxY) {
                maxY = node.bounds.xMax;
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

    private void Noop() {
        if (Input.GetKeyDown(KeyCode.Space)) {
            NextStep();
        }
    }

    private void Step0() {
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
        Node obj4 = new (new Vector2(-1f, 2f), new Vector2(1f, 1f));
        AddNode(root, obj4);
        NextStep();
    }
}
