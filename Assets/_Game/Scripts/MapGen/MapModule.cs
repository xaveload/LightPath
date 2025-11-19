using UnityEngine;
using System.Collections.Generic;

namespace LightPath.MapGen
{
    public enum ModuleType { Room, Corridor, Start, End }

    public class MapModule : MonoBehaviour
    {
        [Header("Settings")]
        public ModuleType type;
        // 이 모듈이 차지하는 사이즈 (충돌 체크용, 대략적인 박스)
        public Vector3 size = new Vector3(10, 5, 10); 
        
        [Header("Connectors")]
        // 이 모듈이 가지고 있는 연결 지점들
        public List<Transform> connectors; 
        
        // 맵 생성 시 이 모듈이 배치된 후 호출될 초기화 함수
        public void Initialize()
        {
            // 나중에 서랍 아이템 생성 로직 등이 여기 들어감
            // 예: ItemSpawner.Spawn(CoreRandom.CurrentSeed);
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireCube(transform.position + Vector3.up * size.y * 0.5f, size);
            
            Gizmos.color = Color.red;
            foreach(var c in connectors)
            {
                if(c != null)
                {
                    Gizmos.DrawSphere(c.position, 0.5f);
                    Gizmos.DrawLine(c.position, c.position + c.forward * 2f);
                }
            }
        }
    }
}