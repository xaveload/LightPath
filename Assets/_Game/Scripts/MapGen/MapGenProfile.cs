using UnityEngine;
using System.Collections.Generic;

namespace LightPath.MapGen
{
    [CreateAssetMenu(menuName = "LightPath/Map Gen Profile")]
    public class MapGenProfile : ScriptableObject
    {
        [Header("기본 그리드 설정")]
        public int mapWidth = 100;
        public int mapHeight = 100;
        public int unitSize = 3; // 그리드 1칸의 실제 유니티 단위 크기 (m)

        [Header("방 생성 규칙")]
        public int totalRoomCount = 20;
        
        [Range(0f, 1f)] 
        [Tooltip("방이 기존 방에 딱 붙어서 생성될 확률")]
        public float roomClusterChance = 0.3f; 
        
        [Tooltip("시작 방 주변에는 다른 방이 생성되지 않는 최소 거리")]
        public int minDistanceFromStart = 10; 

        [Header("사용할 방 데이터")]
        public List<RoomData> availableRooms; 

        [Header("특수 방 설정")]
        public int altarRoomCount = 5;
        
        public bool allowEscapeRoomCluster = false; // 탈출 방 고립 여부
        public int escapeRoomCount = 1;
        public int minDistanceBetweenEscapes = 15; // 탈출구가 여러 개일 때 서로간의 최소 거리
    }
}