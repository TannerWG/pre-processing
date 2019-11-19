using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ESRI.ArcGIS.Geometry;

namespace pre_processing
{
    public class Node //节点
    {
        //成员变量
        public int id { get; set; } //代号
        public IPolyline poly { get; set; } //数据
        public Node parent { get; set; } //父亲节点
        public List<Node> children; //孩子节点

        public int depth; //节点深度
        public double length; //长度：即节点到根节点的距离
        public Posi tag; //方向
        public Posi exp; //暴露端的位置
        public Boolean emptyWindow; //空窗期（记录从该节点到达根节点的路径中是否经过选框之外的节点）

        //构造函数
        public Node() { this.id = 0; this.poly = null; this.parent = null; this.children = null; this.depth = 0; this.length = 0; this.tag = Posi.NONE; this.exp = Posi.NONE; this.emptyWindow = false; }
        public Node(int id = 0, IPolyline data = null, int depth = 0, double length = 0, Node parent = null, List<Node> children = null, Posi tag = Posi.NONE, Posi exp = Posi.NONE, Boolean eptWnd = false)
        {
            this.id = id;
            this.poly = data;
            this.parent = parent;
            this.children = children;
            this.depth = depth;
            this.length = length;
            this.tag = tag;
            this.exp = exp;
            this.emptyWindow = eptWnd;
        }


        //成员函数

        /// <summary>
        /// 获取当前节点的孩子节点
        /// </summary>
        public List<Node> GetChildren() { return this.children; }

        public List<Node> GetChildren(Posi tag)
        {
            List<Node> children = new List<Node>();
            foreach (Node child in this.children)
                if (child.tag == tag) children.Add(child);
            return children;
        }

        public void ShowChildren()
        {
            if (this.GetChildren() == null) Console.WriteLine("该节点没有子节点");
            else
            {
                foreach (Node node in this.GetChildren()) Console.Write(node.poly + " ");
                Console.WriteLine();
            }
            Console.ReadKey();
        }

        /// <summary>
        /// 插入孩子
        /// </summary>
        /// <param name="node">节点</param>
        public Node AddChild(Node node)
        {
            if (this.children == null)
                this.children = new List<Node>() { node };
            else
                if (!this.children.Contains(node))
                    this.children.Add(node);
            return node;
        }

        /// <summary>
        /// 将e作为节点作为当前节点的孩子
        /// </summary>
        /// <param name="polyline">数据</param>
        public Node AddChild(int id, IPolyline polyline, double length, Posi tag = Posi.NONE, Posi exp = Posi.NONE, Boolean eptWnd = false)
        {
            Node child = new Node(id, polyline, this.depth + 1, this.length + length, this, null, tag, exp, eptWnd);
            this.AddChild(child);
            return child;
        }

        /// <summary>
        /// 删除孩子
        /// </summary>
        /// <param name="e">数据</param>
        public Node RemoveChild(Node node)
        {
            this.children.Remove(node);
            return node;
        }

        /// <summary>
        /// 将node作为当前节点的父亲
        /// </summary>
        /// <param name="node">节点</param>
        public Node SetParent(Node node)
        {
            this.parent = node;
            this.depth = node.depth + 1;
            node.AddChild(this);
            return node;
        }

        // 判等器：重载 == 判断节点是否相等
        //public static Boolean operator ==(Node node1, Node node2) { return node1.id == node2.id; }
        // 判等器：重载 == 判断节点是否相等
        //public static Boolean operator !=(Node node1, Node node2) { return node1.id != node2.id; }
    }

    public class Tree //多叉树
    {
        protected int id; //树的代号
        protected int _size; //规模，即节点数量
        protected Node _root;//树根

        public Tree(int id) { this.id = id; this._size = 0; this._root = null; } //构造函数
        public Tree(Node node) { this._root = node; this._size = 1; } //构造函数
        ~Tree() { if (_size > 0) Remove(_root); } //析构函数
        public int Size() { return _size; } //返回规模
        public Boolean Empty() { if (_size == 0) return false; return true; } // 判空
        public Node Root() { return _root; } //获取树根

        /// <summary>
        /// 获取树的复杂程度
        /// </summary>
        public int GetComplexity()
        {
            //以可能诞生的Stroke的数量为树的复杂度
            return FindLeaves(Posi.FROM).Count * FindLeaves(Posi.TO).Count;
        }

        /// <summary>
        /// 将e作为根插入
        /// </summary>
        /// <param name="e">数据</param>
        public Node SetRoot(Node node) { this._root = node; this._size = 1; return node; }

        /// <summary>
        /// 作为根插入
        /// </summary>
        /// <param name="id">id</param>
        /// <param name="poyline">数据</param>
        /// <param name="length">长度</param>
        public Node InsertAsRoot(int id, IPolyline poyline, double length) { this._size = 1; return _root = new Node(id, poyline, 0, length); }

        /// <summary>
        /// 将e作为孩子插入（原无）
        /// </summary>
        /// <param name="node">节点</param>
        /// <param name="id">新节点id</param>
        /// <param name="polyline">新节点数据</param>
        /// <param name="length">长度</param>
        /// <param name="tag">方向</param>
        /// <param name="exp">暴露端</param>
        public Node InsertAsChild(Node node, int id, IPolyline polyline, double length, Posi tag = Posi.NONE, Posi exp = Posi.NONE, Boolean empWnd = false)
        { this._size++; return node.AddChild(id, polyline, length, tag, exp, empWnd); }

        /// <summary>
        /// 更新树root的节点高度
        /// </summary>
        /// <param name="root">根节点</param>
        /// <param name="rDepth">根节点的高度</param>
        public Node UpdateDepth(Node root, int rDepth)
        {
            Queue<Node> Q = new Queue<Node>();
            Q.Enqueue(root);
            while (Q.Count > 0)
            {
                Node currNode = Q.Dequeue();
                if (currNode == root) currNode.depth = rDepth;
                else currNode.depth = currNode.parent.depth + 1;
                if (currNode.children != null) { foreach (Node child in currNode.children) Q.Enqueue(child); } //子节点入列
            }
            return root;
        }

        /// <summary>
        /// 更新树root的节点高度
        /// </summary>
        /// <param name="root">根节点</param>
        /// <param name="rDepth">根节点的高度</param>
        public Tree UpdateDepth(Tree tree, int rDepth)
        {
            UpdateDepth(tree._root, rDepth);
            return tree;
        }

        /// <summary>
        /// 删除以位置node处节点为根的子树，返回该子树原先的规模
        /// </summary>
        /// <param name="node">节点</param>
        public int Remove(Node node)
        {
            //切断来自父节点的指针
            if (node.parent != null) node.parent.RemoveChild(node); //如果有父亲，切断父亲的指针
            int n = RemoveAt(node); this._size -= n; return n; //释放内存
        }

        /// <summary>
        /// 删除子树tree，返回该子树原先的规模
        /// </summary>
        /// <param name="tree">树</param>
        public int Remove(Tree tree) { return Remove(tree._root); }

        /// <summary>
        /// 将子树node从当前树中摘除，并将其转换为一颗独立子树
        /// </summary>
        /// <param name="node">节点</param>
        public Tree Secede(Node node)
        {
            //切断来自父节点的指针
            Node parent = node.parent;
            parent.RemoveChild(node);
            node.parent = null; //父亲归空
            //释放内存
            Tree newtree = new Tree(0); //新建树
            newtree._root = node; //根节点
            int n = RemoveAt(node); //子树节点总数
            newtree._size = n; //赋值子树节点总数
            this._size -= n; //更新此树的节点总数
            UpdateDepth(newtree, 0); //更新高度
            return newtree; //返回新树
        }

        /// <summary>
        /// 计算子树node的节点数目（广度优先算法实现）
        /// </summary>
        /// <param name="node">子树的根节点</param>
        public int RemoveAt(Node node)
        {
            int nodeCount = 0; //子树node的节点总数
            Queue<Node> Q = new Queue<Node>(); //队列
            Q.Enqueue(node); //入队
            while (Q.Count > 0) //在队列为空之前
            {
                Node currNode = Q.Dequeue(); //当前节点
                nodeCount++; //计数
                List<Node> children = currNode.GetChildren(); //获取顶节点的孩子节点
                if (children != null) { foreach (Node child in children) Q.Enqueue(child); } //有孩子：孩子入队
                currNode = null; //释放内存
            }
            Q.Clear(); //释放内存
            return nodeCount;
        }

        /// <summary>
        /// 广度优先算法遍历树
        /// </summary>
        /// <param name="node">子树的根节点</param>
        public List<IPolyline> Traverse()
        {
            List<IPolyline> polys = new List<IPolyline>();
            Queue<Node> Q = new Queue<Node>(); //队列
            Q.Enqueue(this._root); //入队
            while (Q.Count > 0) //在队列为空之前
            {
                Node currNode = Q.Dequeue(); //当前节点
                //Console.WriteLine(currNode.data); //输出
                polys.Add(currNode.poly);
                List<Node> children = currNode.GetChildren(); //获取顶节点的孩子节点
                if (children != null) { foreach (Node child in children) Q.Enqueue(child); } //有孩子：孩子入栈
            }
            Q.Clear(); //释放内存
            //Console.ReadKey();
            return polys;
        }

        /// <summary>
        /// 广度优先算法遍历树
        /// </summary>
        /// <param name="node">子树的根节点</param>
        public List<int> TraverseWithoutRoot()
        {
            List<int> ids = new List<int>();
            Queue<Node> Q = new Queue<Node>(); //队列
            Q.Enqueue(this._root); //入队
            while (Q.Count > 0) //在队列为空之前
            {
                Node currNode = Q.Dequeue(); //当前节点
                //Console.WriteLine(currNode.data); //输出
                ids.Add(currNode.id);
                List<Node> children = currNode.GetChildren(); //获取顶节点的孩子节点
                if (children != null) { foreach (Node child in children) Q.Enqueue(child); } //有孩子：孩子入栈
            }
            Q.Clear(); //释放内存
            //Console.ReadKey();
            return ids;
        }


        /// <summary>
        /// 寻找所有叶子节点
        /// </summary>
        public List<Node> FindLeaves()
        {
            List<Node> leaves = new List<Node>();
            Queue<Node> Q = new Queue<Node>(); //队列
            Q.Enqueue(this._root); //入队
            while (Q.Count > 0) //在队列为空之前
            {
                Node currNode = Q.Dequeue(); //当前节点
                List<Node> children = currNode.GetChildren(); //获取顶节点的孩子节点
                if (children != null) { foreach (Node child in children) Q.Enqueue(child); } //有孩子：孩子入栈
                else leaves.Add(currNode);
            }
            Q.Clear(); //释放内存
            return leaves;
        }

        /// <summary>
        /// 寻找特定颜色的叶子节点
        /// </summary>
        /// <param name="tag">某一侧</param>
        public List<Node> FindLeaves(Posi tag)
        {
            List<Node> leaves = new List<Node>();
            if (tag == Posi.NONE) { leaves.Add(_root); return leaves; } //返回根节点
            if (this._root.children == null) { leaves.Add(_root); return leaves; } //没有孩子，返回根节点

            Queue<Node> Q = new Queue<Node>(); //队列
            foreach (Node child in _root.children)
                if (child.tag == tag) Q.Enqueue(child); //将该方向的节点放入队列
            while (Q.Count > 0) //在队列为空之前
            {
                Node currNode = Q.Dequeue(); //当前节点
                List<Node> children = currNode.GetChildren(); //获取顶节点的孩子节点
                if (children != null)
                {
                    foreach (Node child in children) Q.Enqueue(child);
                    //children.Clear(); 
                } //有孩子：孩子入栈
                else leaves.Add(currNode);
            }
            Q.Clear(); //释放内存
            return leaves;
        }

        /// <summary>
        /// 寻根
        /// </summary>
        /// <param name="node">从该节点寻根</param>
        public List<IPolyline> QueryRoot(Node node)
        {
            List<IPolyline> route = new List<IPolyline>(); //路径数组
            while (node != null) { route.Add(node.poly); node = node.parent; }
            return route;
        }

        // <summary>
        /// 获得FROM端和TO端两两叶子之间的所有路径
        /// </summary>
        public List<List<IPolyline>> QueryRoute()
        {
            List<Node> FLeaves = FindLeaves(Posi.FROM); //FROM端的叶子节点
            List<Node> TLeaves = FindLeaves(Posi.TO); //TO端的叶子节点
            List<List<IPolyline>> FRoutes = new List<List<IPolyline>>(); //FROM的所有路径
            List<List<IPolyline>> TRoutes = new List<List<IPolyline>>(); //TO端的所有路径
            //获得FROM /TO端的所有路径
            for (int i = 0; i < FLeaves.Count; i++)
                FRoutes.Add(QueryRoot(FLeaves[i]));
            for (int i = 0; i < TLeaves.Count; i++)
            {
                List<IPolyline> route = QueryRoot(TLeaves[i]);
                route.Reverse();
                TRoutes.Add(route);
            }

            if (FRoutes.Count == 0) return TRoutes; //如果FROM端没有节点，返回TO端路径
            else if (TRoutes.Count == 0) return FRoutes; //如果TO端没有节点，返回FROM端路径
            else //如果两端都有节点，合并FROM端和TO端的路径
            {
                List<List<IPolyline>> routes = new List<List<IPolyline>>(); //存储路径
                for (int i = 0; i < FRoutes.Count; i++)
                    for (int j = 0; j < TRoutes.Count; j++)
                        routes.Add(FRoutes[i].Union(TRoutes[j]).ToList());
                //释放内存
                FLeaves.Clear(); TLeaves.Clear(); FRoutes.Clear(); TRoutes.Clear();
                return routes; //返回所有路径
            }
        }

        //重载运算符
        public static Boolean operator ==(Tree tree1, Tree tree2)
        { return (tree1.id == tree2.id); }
        public static Boolean operator !=(Tree tree1, Tree tree2)
        { return (tree1.id != tree2.id); }
    }
}
