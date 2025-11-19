using UnityEngine;
using System.Collections.Generic;

namespace LightPath.Utils
{
    public class RoomGizmo : MonoBehaviour
    {
        [Range(2, 10)] public int width = 4;
        [Range(2, 10)] public int height = 4;
        public float unitSize = 3f; // 1칸 = 3m
        public List<Vector2Int> doorSpots;

        private void OnDrawGizmos()
        {
            // 회전된 상태를 반영하여 그리기
            Gizmos.matrix = transform.localToWorldMatrix;

            // 바닥 그리드
            Gizmos.color = new Color(1, 1, 1, 0.3f);
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    Vector3 center = new Vector3(x * unitSize + unitSize/2, 0, y * unitSize + unitSize/2);
                    Gizmos.DrawWireCube(center, new Vector3(unitSize, 0.1f, unitSize));
                }
            }

            // 전체 외곽선 (노란색) - 프리팹 모델이 이 안에 꽉 차야 함
            Gizmos.color = Color.yellow;
            Vector3 totalCenter = new Vector3(width * unitSize / 2, 0, height * unitSize / 2);
            Vector3 totalSize = new Vector3(width * unitSize, 1f, height * unitSize);
            Gizmos.DrawWireCube(totalCenter, totalSize);

            // 문 위치 (초록색 구)
            Gizmos.color = Color.green;
            if (doorSpots != null)
            {
                foreach (var spot in doorSpots)
                {
                    Vector3 spotPos = new Vector3(spot.x * unitSize + unitSize/2, 0.5f, spot.y * unitSize + unitSize/2);
                    Gizmos.DrawSphere(spotPos, 0.5f);
                }
            }
        }
    }
}