using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.Experimental.GraphView;
using UnityEngine;

public interface IDataContainer<T> {
    T ObjectData { get; }
    void SetData(T data);
}

public interface IBoundsProvider {
    Rect GetBounds();
}

public interface IRTreeNode {
    IRTreeNode Parent { get; }
    Rect Bounds { get; }
    List<IRTreeNode> Children { get; }
    List<IRTreeNode> Leaves { get; }
    void AddLeaf(IRTreeNode leafNode);
    void AddChild(IRTreeNode node);
    void AddChild(IRTreeNode node, Rect preCalculatedBounds);
    void RemoveChild(IRTreeNode node);
    void DetachFromTree();
    void SetParent(IRTreeNode parent);
    void SetBounds(Rect bounds);
    void UpdateBounds(IRTreeNode node);
}

public class Node<T> : IRTreeNode, IDataContainer<T> where T : IBoundsProvider, IEquatable<T> {
    public T ObjectData { get; private set; }
    public IRTreeNode Parent { get; private set; }
    public Rect Bounds { get; private set; }
    public List<IRTreeNode> Children { get; private set; }
    public List<IRTreeNode> Leaves { get; private set; }

    public Node(Node<T> parent, Vector2 center, Vector2 size, T objectData = default) :
        this(parent, new Rect(center, size), objectData) {
    }

    public Node(Node<T> parent, Rect bounds, T objectData = default) {
        Parent = parent;
        Bounds = bounds;
        ObjectData = objectData;

        Children = new List<IRTreeNode>();
        Leaves = new List<IRTreeNode>();
    }

    public void AddLeaf(IRTreeNode leafNode) {
        Leaves.Add(leafNode);
        UpdateBounds(leafNode);
    }

    public void AddChild(IRTreeNode node) {
        node.SetParent(this);
        Children.Add(node);
        UpdateBounds(node);
    }

    public void AddChild(IRTreeNode node, Rect preCalculatedBounds) {
        node.SetParent(this);
        Children.Add(node);
        Bounds = preCalculatedBounds;
    }    

    public void RemoveChild(IRTreeNode node) {
        node.SetParent(null);
        Children.Remove(node);
    }

    public void DetachFromTree() {
        Parent.RemoveChild(this);
    }

    public void SetParent(IRTreeNode parent) {
        Parent = parent;
    }

    public void SetBounds(Rect bounds) {
        Bounds = bounds;
    }

    public void UpdateBounds(IRTreeNode node) {
        Bounds = Bounds.ExpandedToContain(node.Bounds);
    }

    public void SetData(T data) {
        ObjectData = data;
    }
}

public struct MinMaxData {
    public float min, max;
    public IRTreeNode minRef, maxRef;
}

public class Rtree<T> where T : IBoundsProvider, IEquatable<T> {
    //max densisty per leaf
    private int m = 50;

    private float worldSize;

    public Node<T> Root { get; private set; }
    private List<Color> colors = new() { Color.white };
    private List<Node<T>> queryResultList = new();

    public Rtree(float worldSize, int density){
        this.worldSize = worldSize;
        this.m = density;
        Root = new Node<T>(null, new Vector2(-worldSize / 2f, -worldSize / 2f), new Vector2(worldSize, worldSize));
    }

    public bool SelectBestCaseFromAreaAndOverlapScore(
        bool xPriority,
        float totalXCaseArea, 
        float totalYCaseArea,
        Rect leafABoundsXCase,
        Rect leafBBoundsXCase,
        Rect leafABoundsYCase,
        Rect leafBBoundsYCase
        ) {

        int xScore = 0;
        int yScore = 0;

        //compare cases by area
        if (totalXCaseArea < totalYCaseArea) xScore++;
        else if (totalXCaseArea > totalYCaseArea) yScore++;

        // check overlaps
        if (!leafABoundsXCase.Overlaps(leafBBoundsXCase)) xScore++;
        if (!leafABoundsYCase.Overlaps(leafBBoundsYCase))  yScore++;

        bool xSelected = xPriority;

        if (xScore > yScore) xSelected = true;
        else if (yScore > xScore) xSelected = false;

        return xSelected;
    }

    public void SplitLeafNode(ref IRTreeNode parent) {
        var minMaxData = GetMinMax(ref parent);

        parent.RemoveChild(minMaxData.minRef);
        parent.RemoveChild(minMaxData.maxRef);

        //create 2 leafs, to add furthest nodes apart
        Node<T> leafA = new(parent as Node<T>, minMaxData.minRef.Bounds);
        leafA.AddChild(minMaxData.minRef);

        Node<T> leafB = new(parent as Node<T>, minMaxData.maxRef.Bounds);
        leafB.AddChild(minMaxData.maxRef);        

        int safety = 0;
        while (parent.Children.Count > 1 && safety < 1000) {
            safety++;

            var minMaxX = GetMinMaxX(parent);
            var minMaxY = GetMinMaxY(parent);

            Rect leafAXCase = leafA.Bounds.ExpandedToContain(minMaxX.minRef.Bounds);
            Rect leafBXCase = leafB.Bounds.ExpandedToContain(minMaxX.maxRef.Bounds);
            float totalXCaseArea = (leafAXCase.width * leafAXCase.height) + (leafBXCase.width * leafBXCase.height);

            Rect leafAYCase = leafA.Bounds.ExpandedToContain(minMaxY.minRef.Bounds);
            Rect leafBYCase = leafB.Bounds.ExpandedToContain(minMaxY.maxRef.Bounds);
            float totalYCaseArea = (leafAYCase.width * leafAYCase.height) + (leafBYCase.width * leafBYCase.height);

            bool xSelected = SelectBestCaseFromAreaAndOverlapScore(
                true, totalXCaseArea, totalYCaseArea, leafAXCase, leafBXCase, leafAYCase, leafBYCase);

            IRTreeNode minRef = xSelected ? minMaxX.minRef : minMaxY.minRef;
            IRTreeNode maxRef = xSelected ? minMaxX.maxRef : minMaxY.maxRef;
            Rect leafABounds = xSelected ? leafAXCase : leafAYCase;
            Rect leafBBounds = xSelected ? leafBXCase : leafBYCase;

            parent.RemoveChild(minRef);
            parent.RemoveChild(maxRef);

            leafA.AddChild(minRef, leafABounds);
            leafB.AddChild(maxRef, leafBBounds);            
        }

        //deal with last node in case of odd M
        if (parent.Children.Count > 0) {
            IRTreeNode lastNode = parent.Children[0];
            IRTreeNode leafNode = SelectLeafNode(lastNode.Bounds, leafA, leafB);

            parent.RemoveChild(lastNode);
            leafNode.AddChild(lastNode);
        }
        parent.Leaves.Add(leafA);
        parent.Leaves.Add(leafB);
    }

    public IRTreeNode SelectLeafNode(Rect nodeBounds, IRTreeNode leafA, IRTreeNode leafB) {
        //check if the bounds already overlap any of the nodes, favoring A
        if (leafA.Bounds.Overlaps(nodeBounds)) return leafA;
        if (leafB.Bounds.Overlaps(nodeBounds)) return leafB;

        //get closest by distance
        var center = nodeBounds.center;
        float distanceToA = Vector2.Distance(leafA.Bounds.center, center);
        float distanceToB = Vector2.Distance(leafB.Bounds.center, center);
        if (distanceToA < distanceToB) return leafA;

        return leafB;
    }

    public void AddObject(T obj) {
        Node<T> newNode = new Node<T>(null, obj.GetBounds(), obj);

        AddNode(Root, newNode);
    }

    private void AddNode(IRTreeNode parent, IRTreeNode node) {
        //check if parent is leaf
        if (parent.Leaves.Count == 0) {
            // parent is leaf, try to add node
            parent.AddChild(node);

            //check capacity
            if (parent.Children.Count >= m) SplitLeafNode(ref parent);
            return;
        }

        // current node is not a leaf
        // start by checking if we overlap any of the leaves
        foreach (var leaf in parent.Leaves) {
            if (leaf.Bounds.Overlaps(node.Bounds)) {
                AddNode(leaf, node);
                parent.UpdateBounds(leaf);
                return;
            }
        }

        // if no leaf is overlaping, choose by distance
        float minDist = float.PositiveInfinity;
        float d;
        IRTreeNode minRef = null;
        foreach (var leaf in parent.Leaves) {
            d = Vector2.Distance(leaf.Bounds.center, node.Bounds.center);
            if (d < minDist) {
                minDist = d;
                minRef = leaf;
            }
        }

        AddNode(minRef, node);
        parent.UpdateBounds(minRef);
    }

    public void RemoveObject(T obj) {
        queryResultList.Clear();

        QueryRegion(Root, obj.GetBounds(), queryResultList);

        foreach (var node in queryResultList) {
            if (node.ObjectData.Equals(obj)) {
                node.DetachFromTree();
                return;
            }
        }
    }

    public void QueryObjects(Rect region, List<T> result) {
        QueryRegion(Root, region, result);
    }

    public void QueryObjectsNonRecursive(Rect region, List<T> resultList) {
        resultList.Clear();
        IRTreeNode pointer;
        Stack<IRTreeNode> stack = new Stack<IRTreeNode>();

        stack.Push(Root);

        while (stack.TryPop(out pointer)) {
            if (region.Overlaps(pointer.Bounds)) {
                if (pointer.Leaves.Count > 0) {
                    foreach (IRTreeNode leaf in pointer.Leaves) {
                        stack.Push(leaf);
                    }
                } else {
                    foreach (IRTreeNode node in pointer.Children) {
                        if (region.Overlaps(node.Bounds)) resultList.Add((node as Node<T>).ObjectData);
                    }
                }
            }
        }
    }

    public void QueryRegionNonRecursive(Rect region, List<Node<T>> resultList) {
        resultList.Clear();
        IRTreeNode pointer;
        Stack<IRTreeNode> stack = new Stack<IRTreeNode>();

        stack.Push(Root);

        while (stack.TryPop(out pointer)) {
            if (region.Overlaps(pointer.Bounds)) {
                if (pointer.Leaves.Count > 0) {
                    foreach (IRTreeNode leaf in pointer.Leaves) {
                        stack.Push(leaf);
                    }
                } else {
                    foreach (IRTreeNode node in pointer.Children) {
                        if (region.Overlaps(node.Bounds)) resultList.Add(node as Node<T>);
                    }
                }
            }
        }
    }

    private void QueryRegion(IRTreeNode current, Rect region, List<T> resultList) {
        resultList.Clear();

        if (current.Leaves.Count > 0) {
            //not leaf node
            foreach (var leaf in current.Leaves) {
                if (leaf.Bounds.Overlaps(region)) {
                    QueryRegion(leaf, region, resultList);
                }
            }
        }

        foreach (var child in current.Children) {
            if (child.Bounds.Overlaps(region)) {
                resultList.Add((child as Node<T>).ObjectData);
            }
        }
    }

    

    private void QueryRegion(IRTreeNode current, Rect region, List<Node<T>> resultList) {
        resultList.Clear();

        if(current.Leaves.Count > 0) {
            //not leaf node
            foreach (var leaf in current.Leaves) {
                if (leaf.Bounds.Overlaps(region)) {
                    QueryRegion(leaf, region, resultList);
                }
            }
        }

        foreach (var child in current.Children) {
            if (child.Bounds.Overlaps(region)) {
                resultList.Add(child as Node<T>);
            }
        }
    }

    public MinMaxData GetMinMaxX(IRTreeNode parent) {
        MinMaxData minMaxData = new();
        //set def values
        float minX = float.PositiveInfinity;
        float maxX = float.NegativeInfinity;
        IRTreeNode minXref = null;
        IRTreeNode maxXref = null;

        foreach (IRTreeNode node in parent.Children) {
            if (node.Bounds.xMin < minX) {
                minX = node.Bounds.xMin;
                minXref = node;
            }

            if (node.Bounds.xMax > maxX) {
                maxX = node.Bounds.xMax;
                maxXref = node;
            }
        }

        minMaxData.min = minX;
        minMaxData.max = maxX;
        minMaxData.minRef = minXref;
        minMaxData.maxRef = maxXref;

        return minMaxData;
    }

    public MinMaxData GetMinMaxY(IRTreeNode parent) {
        MinMaxData minMaxData = new();
        //set def values
        float minY = float.PositiveInfinity;
        float maxY = float.NegativeInfinity;
        IRTreeNode minYref = null;
        IRTreeNode maxYref = null;

        foreach (IRTreeNode node in parent.Children) {
            if (node.Bounds.yMin < minY) {
                minY = node.Bounds.yMin;
                minYref = node;
            }

            if (node.Bounds.yMax > maxY) {
                maxY = node.Bounds.yMax;
                maxYref = node;
            }
        }

        minMaxData.min = minY;
        minMaxData.max = maxY;
        minMaxData.minRef = minYref;
        minMaxData.maxRef = maxYref;

        return minMaxData;
    }

    public MinMaxData GetMinMax(ref IRTreeNode parent) {
        //iterate saving min/max x/y with ref to owners
        var minMaxX = GetMinMaxX(parent);
        var minMaxY = GetMinMaxY(parent);

        float xRange = minMaxX.max - minMaxX.min;
        float yRange = minMaxY.max - minMaxY.min;

        if (xRange > yRange) {
            return minMaxX;
        }
        else
        {
            return minMaxY;
        }
    }    
}
