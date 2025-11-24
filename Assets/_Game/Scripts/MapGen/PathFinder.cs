using UnityEngine;
using System.Collections.Generic;

namespace LightPath.Utils
{
    public static class Pathfinder
    {
        private class Node
        {
            public Vector2Int pos;
            public Node parent;
            public int gCost; 
            public int hCost; 
            public int FCost => gCost + hCost;

            public Node(Vector2Int _pos) { pos = _pos; }
        }

        public static List<Vector2Int> FindPath(int[,] grid, Vector2Int startPos, Vector2Int targetPos)
        {
            int width = grid.GetLength(0);
            int height = grid.GetLength(1);

            if (IsOutOfBounds(startPos, width, height) || IsOutOfBounds(targetPos, width, height)) return null;

            List<Node> openSet = new List<Node>();
            HashSet<Vector2Int> closedSet = new HashSet<Vector2Int>();
            Dictionary<Vector2Int, Node> nodeMap = new Dictionary<Vector2Int, Node>();

            Node startNode = new Node(startPos);
            openSet.Add(startNode);
            nodeMap.Add(startPos, startNode);

            while (openSet.Count > 0)
            {
                Node currentNode = openSet[0];
                for (int i = 1; i < openSet.Count; i++)
                {
                    if (openSet[i].FCost < currentNode.FCost || 
                       (openSet[i].FCost == currentNode.FCost && openSet[i].hCost < currentNode.hCost))
                    {
                        currentNode = openSet[i];
                    }
                }

                openSet.Remove(currentNode);
                closedSet.Add(currentNode.pos);

                if (currentNode.pos == targetPos) return RetracePath(currentNode);

                foreach (Vector2Int dir in new Vector2Int[] { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right })
                {
                    Vector2Int neighborPos = currentNode.pos + dir;

                    if (IsOutOfBounds(neighborPos, width, height)) continue;
                    if (closedSet.Contains(neighborPos)) continue;
                    
                    // 장애물(방:1) 체크. (도착지는 허용)
                    bool isRoom = grid[neighborPos.x, neighborPos.y] == 1;
                    if (isRoom && neighborPos != targetPos) 
                    {
                        // 시작점 바로 옆이 방인 경우(나가는 중)는 허용
                        if (currentNode.pos != startPos) continue; 
                    }

                    // [복잡도 조절 핵심]
                    // 이미 복도(2)인 곳은 비용 1, 빈 땅(0)은 비용 5
                    // -> 이러면 최대한 기존 길을 타고 가려고 해서 맵이 깔끔해짐
                    int moveCost = (grid[neighborPos.x, neighborPos.y] == 2) ? 1 : 5; 
                    
                    int newMovementCost = currentNode.gCost + moveCost;

                    Node neighborNode;
                    if (nodeMap.TryGetValue(neighborPos, out neighborNode))
                    {
                        if (newMovementCost < neighborNode.gCost)
                        {
                            neighborNode.gCost = newMovementCost;
                            neighborNode.parent = currentNode;
                        }
                    }
                    else
                    {
                        neighborNode = new Node(neighborPos);
                        neighborNode.gCost = newMovementCost;
                        neighborNode.hCost = GetDistance(neighborPos, targetPos) * 5; // 휴리스틱 가중치
                        neighborNode.parent = currentNode;
                        
                        openSet.Add(neighborNode);
                        nodeMap.Add(neighborPos, neighborNode);
                    }
                }
            }
            return null;
        }

        static bool IsOutOfBounds(Vector2Int pos, int width, int height)
        {
            return pos.x < 0 || pos.x >= width || pos.y < 0 || pos.y >= height;
        }

        static List<Vector2Int> RetracePath(Node endNode)
        {
            List<Vector2Int> path = new List<Vector2Int>();
            Node currentNode = endNode;
            while (currentNode != null)
            {
                path.Add(currentNode.pos);
                currentNode = currentNode.parent;
            }
            path.Reverse();
            return path;
        }

        static int GetDistance(Vector2Int a, Vector2Int b)
        {
            return Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);
        }
    }
}