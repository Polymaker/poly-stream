using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace System.Collections.Generic
{
    public static class CollectionExtensions
    {

        public static void RemoveRange<T>(this LinkedList<T> list, IEnumerable<LinkedListNode<T>> nodes)
        {
            var nodeArray = nodes.ToArray();//copy to array to prevent collection changed exception
            for (int i = 0; i < nodeArray.Length; i++)
            {
                if(nodeArray[i].List == list)
                    list.Remove(nodeArray[i]);
            }
        }

        public static IEnumerable<LinkedListNode<T>> GetNodesInBetween<T>(this LinkedList<T> list, LinkedListNode<T> from, LinkedListNode<T> to, bool inclusive = true)
        {
            if (from.List != list || to.List != list)
                throw new Exception();

            var nodeList = new List<LinkedListNode<T>>();

            var current = from;
            bool found = false;
            do
            {
                nodeList.Add(current);
                if ((found = (current == to)))
                    break;
            }
            while ((current = current.Next) != null);

            if (!found)
                nodeList.Clear();
            if (!inclusive)
            {
                nodeList.Remove(from);
                nodeList.Remove(to);
            }
            return nodeList;
        }
    }
}
