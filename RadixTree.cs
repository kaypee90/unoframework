namespace unoframework;

public class RadixTree<T>
{
    private class Node
    {
        public string Key { get; set; }
        
        public T Value { get; set; }
        
        public bool IsTerminal { get; set; }
        
        public Dictionary<char, Node> Children { get; set; }

        public Node(string key)
        {
            Key = key;
            Children = new Dictionary<char, Node>();
        }
    }

    private Node _root;

    public RadixTree()
    {
        _root = new Node(string.Empty);
    }

    public void Insert(string key, T value)
    {
        var current = _root;
        foreach (var c in key)
        {
            if (!current.Children.ContainsKey(c))
            {
                current.Children[c] = new Node(key);
            }
            current = current.Children[c];
        }

        current.IsTerminal = true;
        current.Value = value;
    }

    public T Search(string key)
    {
        var current = _root;
        foreach (var c in key)
        {
            if (!current.Children.ContainsKey(c))
            {
                return default(T);
            }
            current = current.Children[c];
        }

        return current.IsTerminal ? current.Value : default(T);
    }
}