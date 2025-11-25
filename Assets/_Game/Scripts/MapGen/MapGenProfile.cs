using UnityEngine;
using System.Collections.Generic;

namespace LightPath.MapGen
{
    public enum MapLayoutType
    {
        SpiderWeb = 0,  // 거미줄 (무작위 방이 동시에 지점 연결)
        Subway = 1,     // 지하철 (이전 방에서 1대1 지점을 정해 연결)
        AntHill = 2     // 개미굴 (중앙부터 지점 연결)
    }
    
    [CreateAssetMenu(fileName = "NewMapProfile", menuName = "LightPath/Map Profile")]
    public class MapGenProfile : ScriptableObject
    {
        [Header("맵 구성")]
        [Tooltip("맵의 전체적인 연결 구조를 결정합니다.")] public MapLayoutType layoutType = MapLayoutType.SpiderWeb;
        [Tooltip("맵의 가로 그리드 크기")] public int mapWidth = 200;
        [Tooltip("맵의 세로 그리드 크기")] public int mapHeight = 200;
        [Tooltip("그리드 한 칸의 실제 유니티 월드 크기")] public float unitSize = 3f;

        [Header("기본 방 설정")]
        [Tooltip("생성할 총 방의 개수")] public int totalRoomCount = 50;
        [Tooltip("총 방 중 탈출구(Escape) 방의 개수")] public int escapeRoomCount = 5;
        [Tooltip("각 탈출구 방 사이에 떨어져 있어야 할 최소 거리")] public int minDistBetweenEscapes = 50;
        [Tooltip("총 방 중 제단(Altar) 방의 개수")] public int altarRoomCount = 5;
        [Tooltip("각 제단 방 사이에 떨어져 있어야 할 최소 거리")] public int minDistBetweenAltars = 20;
        [Tooltip("방들이 서로 붙어서 생성될 확률 (0~1)")] [Range(0f, 1f)] public float roomClusterChance = 0.1f;
        [Tooltip("시작 방으로부터 다른 방들이 떨어져 있어야 하는 최소 거리")] public int minDistanceFromStart = 5;
        
        [Header("방 특이성 설정")]
        [Tooltip("방 주변에 복도를 두를 확률")] [Range(0f, 1f)] public float roomRingChance = 0.2f; 
        [Tooltip("방 하나에 기본 문 외에 추가 문이 생길 확률 리스트 (인덱스 0: 2개, 1: 3개...)")] [Range(0f, 1f)] public List<float> extraDoorCountChances; 
        [Tooltip("2칸 이상의 가능한 문 소켓 뭉치에서 이중 문이 생성될 가중치")] public float doubleDoorChance = 0.5f;
        [Tooltip("생성 가능한 방 데이터 목록")] public List<RoomData> availableRooms;

        [Header("최외곽, 그리드 보정")]
        [Tooltip("맵 최외곽을 감싸는 순환로 생성 여부")] public bool useOuterLoop = true;

        [Tooltip("프로시저럴 그리드 대신 텍스처의 알파값을 가이드로 사용할지 여부")] 
        public bool useTextureGuide = false;
        
        [Tooltip("가이드로 사용할 텍스처 (Read/Write Enabled 필수!)")] 
        public Texture2D guideTexture;
        
        [Tooltip("텍스처의 알파값이 이 수치 이상일 때 가이드로 인식 (0~1)")] 
        [Range(0f, 1f)] public float guideAlphaThreshold = 0.5f;
        
        [Tooltip("가로 방향(세로선) 분할 개수")] public int gridDivisionX = 4; 
        [Tooltip("세로 방향(가로선) 분할 개수")] public int gridDivisionY = 4;
        
        [Tooltip("그리드 가이드 라인 위치의 랜덤 오차 범위 (+- 칸수)")] public int gridOffsetRange = 20;

        [Tooltip("복도가 직선으로 뻗을 수 있는 최대 칸수 (초과 시 꺾임)")] public int pathStraightLimit = 15;

        [Tooltip("그리드 형태 보존성 (가이드 라인을 따를 때 이동 비용 할인량)")] public int guideLineDiscount = 3;
        

        [Header("복도 연결 규칙")]
        [Tooltip("연결 거리의 평균 성향 (0: 가까운 곳, 1: 먼 곳)")] [Range(0f, 1f)] public float longPathProb = 0.2f;
        [Tooltip("연결 거리의 표준편차 (0: 목표 거리 고수, 1: 매우 랜덤하게 퍼짐)")] [Range(0f, 1f)] public float distanceVariance = 0.3f;
        [Tooltip("연결할 소켓과 소켓 사이의 최소 거리(칸)")] public int minSocketDistance = 2;
        [Tooltip("기존 복도를 재사용할 때의 이동 비용 (낮을수록 기존 길 선호)")] public int existingPathCost = 1;
        [Tooltip("샛길 뚫는 시도의 총 횟수 (맵 넓이의 절반 권장)")] [Range(0, 1000)] public int branchingAttempts = 100;
        [Tooltip("샛길 뚫는 시도에서 샛길을 뚫을 확률")] [Range(0f, 1f)] public float extraConnectionChance = 0.1f;

        [Header("복도 특이성 설정")]
        [Tooltip("복도를 2칸 너비로 확장할 확률")] [Range(0f, 1f)] public float wideCorridorChance = 0.4f;
        [Tooltip("복도 중간에 문을 설치할 확률")] [Range(0f, 1f)] public float corridorDoorChance = 0.1f;
        [Tooltip("복도 문 사이의 최소 거리")] public int corridorDoorDistance = 5;

        [Header("바닥 설정")]
        [Tooltip("기본 복도 바닥 프리팹")] public GameObject corridorFloorBasic;
        [Tooltip("장식용 복도 바닥 프리팹 목록")] public List<GameObject> corridorFloorDecos;
        [Tooltip("장식 바닥이 생성될 확률")] public float floorDecoChance = 0.1f;
        [Tooltip("바닥 생성 시 위치 오프셋")] public Vector3 corridorFloorOffset = Vector3.zero;

        [Header("천장 설정")]
        [Tooltip("천장에 생성될 오브젝트 목록과 가중치")] public List<CeilingObjectData> ceilingObjects; 
        [Tooltip("바닥으로부터 천장 오브젝트까지의 높이")] public float lightHeight = 3.5f; 
        [Tooltip("천장 오브젝트 간의 최소 거리(칸)")] public int minLightSpacing = 3;  
        [Tooltip("배치 가능한 위치에 천장 오브젝트가 생성될 확률")] [Range(0f, 1f)] public float lightSpawnChance = 0.8f; 

        [Header("벽 설정")]
        [Tooltip("기본 복도 벽 데이터 (필수)")] public WallPrefabData corridorWallBasic;
        [Tooltip("장식용 벽 데이터 목록")] public List<WallPrefabData> wallPrefabs;

        [Header("문 설정")]
        [Tooltip("단일 여닫이문 데이터 목록")] public List<DoorPrefabData> swingDoors;
        [Tooltip("양방향 여닫이문 데이터 목록")] public List<DoorPrefabData> doubleDoors;
        
        [Header("시작 방 설정")]
        [Tooltip("시작 방의 위치 고정 여부")] public bool useFixedStartPos = true;
        [Tooltip("시작 방의 고정 그리드 좌표")] public Vector2Int fixedStartGridPos = new Vector2Int(100, 5);

        [Header("스테이지 특수 설정 (복층, 히든)")]
        [Tooltip("맵 생성 시 무조건 배치될 고정 방 목록 (계단, 히든 룸 등)")] public List<FixedRoomEntry> fixedLayoutRooms;

        [Tooltip("맵 생성 시 무조건 비워둘 공간 목록 (낙차 기믹, 뚫린 천장 연출 등)")] public List<ReservedSpaceEntry> reservedSpaces;
    }

    [System.Serializable] 
    public class WallPrefabData 
    { 
        [Tooltip("생성할 벽 프리팹")] public GameObject prefab; 
        [Tooltip("해당 벽이 차지하는 가로 칸 수")] public int size = 1; 
        [Tooltip("해당 벽이 선택될 가중치")] public float chance = 1f;
        [Tooltip("기능성 장식 벽인지")] public bool isFunctional = false;
        [Tooltip("기능성 장식 벽이 맞다면 깊이가 얼만지")] public int depth = 0;
    }
    
    [System.Serializable] 
    public class DoorPrefabData 
    { 
        [Tooltip("생성할 문 프리팹")] public GameObject prefab; 
        [Tooltip("해당 문이 선택될 가중치")] public float weight = 1f; 
        [Tooltip("해당 문이 방 입구에 생성될 수 있는지 여부")] public bool roomSpawn = true; 
        [Tooltip("해당 문이 복도 중간에 생성될 수 있는지 여부")] public bool corridorSpawn = true; 
    }
    
    [System.Serializable] 
    public class CeilingObjectData 
    { 
        [Tooltip("생성할 천장 오브젝트 프리팹")] public GameObject prefab; 
        [Tooltip("해당 오브젝트가 선택될 가중치")] public float weight = 1f; 
    }

    [System.Serializable]
    public class FixedRoomEntry // 오브젝트 고정 데이터
    {
        [Tooltip("배치할 방 데이터")] public RoomData roomData;
        [Tooltip("배치할 그리드 좌표 (왼쪽 아래 기준)")] public Vector2Int position;
        [Tooltip("방 회전 각도 (0, 90, 180, 270)")] public int rotationAngle;
    }


    [System.Serializable]
    public class ReservedSpaceEntry // 공간 예약 데이터 (구멍 뚫을 곳)
    {
        [Tooltip("비워둘 영역의 좌표와 크기 (x, y, w, h)")] public RectInt region;
    }
}