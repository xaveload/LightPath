using UnityEngine;
using System.Collections.Generic;

namespace LightPath.MapGen
{
    public enum RoomType 
    { 
        Normal, 
        Start, 
        Altar,  // 제단 (곡옥)
        Escape, // 탈출구
        Special 
    }

    [CreateAssetMenu(fileName = "NewRoomData", menuName = "LightPath/Room Data")]
    public class RoomData : ScriptableObject
    {
        [Header("기본 정보")]
        public string id;
        public RoomType type;
        public GameObject prefab;

        [Header("그리드 크기 (1x1 단위)")]
        [Range(2, 10)] public int width = 4;
        [Range(2, 10)] public int height = 4;

        [Header("문 생성 정보")]
        // (x, y) 좌표 기준 문이 생성될 수 있는 후보 위치들
        public List<Vector2Int> possibleDoorSpots;
        // 해당 위치가 미닫이문(2칸 연결)이 될 수 있는지 여부
        public List<bool> canBeSliding; 

        /// <summary>
        /// 에디터나 디버그용 사이즈 계산 함수
        /// </summary>
        public Vector3 GetSizeWorld()
        {
            float unitScale = 3f; // 기본 3m 가정
            return new Vector3(width * unitScale, 4f, height * unitScale);
        }
    }
}