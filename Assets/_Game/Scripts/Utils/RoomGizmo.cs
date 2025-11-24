using UnityEngine;
using System.Collections.Generic;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace LightPath.Utils
{
    public class RoomGizmo : MonoBehaviour
    {
        public float unitSize = 3f;
        public int width = 4;
        public int height = 4;

        [System.Serializable]
        public struct DoorInfo
        {
            public Vector2Int pos;
            public Vector2Int dir; // [필수 추가] 방향 정보
            public bool canBeSliding;
        }

        public List<DoorInfo> doors = new List<DoorInfo>();

        [ContextMenu("Auto Find Doors")]
        public void FindDoors()
        {
            #if UNITY_EDITOR
            Undo.RecordObject(this, "Find Doors Auto");
            #endif

            doors.Clear();
            var sockets = GetComponentsInChildren<RoomSocket>();
            
            foreach (var socket in sockets)
            {
                Vector3 localPos = socket.transform.position - transform.position;
                
                // 1. 위치 계산 (FloorToInt로 정확하게)
                int x = Mathf.FloorToInt((localPos.x / unitSize) + 0.01f);
                int y = Mathf.FloorToInt((localPos.z / unitSize) + 0.01f);
                x = Mathf.Clamp(x, 0, width - 1);
                y = Mathf.Clamp(y, 0, height - 1);

                // 2. [핵심] 소켓의 회전(Forward)을 보고 방향 결정
                // (소켓의 파란 화살표가 가리키는 쪽이 복도다!)
                Vector3 fwd = socket.transform.forward;
                Vector2Int d = Vector2Int.zero;

                if (Mathf.Abs(fwd.x) > Mathf.Abs(fwd.z))
                    d = (fwd.x > 0) ? Vector2Int.right : Vector2Int.left;
                else
                    d = (fwd.z > 0) ? Vector2Int.up : Vector2Int.down;

                doors.Add(new DoorInfo 
                { 
                    pos = new Vector2Int(x, y), 
                    dir = d, 
                    canBeSliding = socket.isSlidingCandidate 
                });
            }

            #if UNITY_EDITOR
            EditorUtility.SetDirty(this);
            #endif
            Debug.Log($"[RoomGizmo] 문 {doors.Count}개 (위치+방향) 저장 완료!");
        }
        
        private void OnDrawGizmos()
        {
             // (기존 기즈모 코드 유지)
             Matrix4x4 rotationMatrix = Matrix4x4.TRS(transform.position, transform.rotation, transform.lossyScale);
             Gizmos.matrix = rotationMatrix; 
             Gizmos.color = Color.yellow;
             Vector3 center = new Vector3(width * unitSize * 0.5f, 1.0f, height * unitSize * 0.5f);
             Vector3 size = new Vector3(width * unitSize, 2.0f, height * unitSize);
             Gizmos.DrawWireCube(center, size);
        }
    }
}