using UnityEngine;
using System.Collections.Generic;
using System.Linq; 
using LightPath.Utils; 

namespace LightPath.MapGen
{
    public class MapGenerator : MonoBehaviour
    {
        public static MapGenerator Instance;

        [Header("설정 프로필")]
        public MapGenProfile profile;

        [Header("디버그")]
        public bool autoGenerateOnStart = true;

        // --- 내부 데이터 ---
        private int[,] grid;
        private List<RoomInstance> placedRooms = new List<RoomInstance>();

        // 생성될 방의 상태 정보를 담는 내부 클래스
        [System.Serializable]
        public class RoomInstance
        {
            public RoomData data;
            public RectInt bounds;       // 그리드 좌표 (x, y, w, h)
            public RoomType currentType; 
            public GameObject spawnedObject;
            
            public int rotationAngle;      // 0, 90, 180, 270
            public int sortPriority;       // 하이어라키 정렬 순위
            public float randomTieBreaker; // 거리 동점 시 순서 보장용 난수
        }

        private void Awake()
        {
            Instance = this;
        }

        private void Start()
        {
            // GameManager 초기화 이후 실행
            if (autoGenerateOnStart) GenerateMap();
        }

        public void GenerateMap()
        {
            // 안전장치: GameManager 없이 테스트할 경우 임시 시드 사용
            if (CoreRandom.CurrentSeed == 0)
            {
                Debug.LogWarning("GameManager가 없거나 시드가 초기화되지 않았습니다. 임시 시드(1234)를 사용합니다.");
                CoreRandom.Initialize(1234);
            }

            Debug.Log($">>> [MapGenerator] 맵 생성 시작 (Seed: {CoreRandom.CurrentSeed})");
            
            grid = new int[profile.mapWidth, profile.mapHeight];
            placedRooms.Clear();

            // 1. 뼈대 잡기 (위치, 회전, 뭉침)
            PlaceRooms();

            // 2. 역할 부여 (제단, 탈출구 등)
            AssignRoomTypes();

            // 3. 건설 (프리팹 생성 및 좌표 보정)
            SpawnRoomPrefabs();

            Debug.Log(">>> [MapGenerator] 맵 생성 완료");
        }

        // --- Step 1: 방 위치 및 회전 잡기 ---
        void PlaceRooms()
        {
            // A. [시작 방] 맵 하단 중앙에 고정 배치
            RoomData startData = profile.availableRooms.Find(r => r.type == RoomType.Start);
            if(startData == null) { Debug.LogError("Start Data Missing"); return; }

            int startX = (profile.mapWidth - startData.width) / 2;
            int startY = 5; 
            RectInt startRect = new RectInt(startX, startY, startData.width, startData.height);

            RoomInstance startRoom = new RoomInstance();
            startRoom.data = startData;
            startRoom.bounds = startRect;
            startRoom.currentType = RoomType.Start; 
            startRoom.rotationAngle = 0;
            startRoom.randomTieBreaker = 0f; 
            startRoom.sortPriority = 0;

            placedRooms.Add(startRoom);
            MarkGrid(startRect, 1);
            
            Vector2 startCenter = new Vector2(startRect.center.x, startRect.center.y);

            // B. [일반 방] 배치 (총 개수만큼 반복)
            RoomData normalData = profile.availableRooms.Find(r => r.type == RoomType.Normal);
            if(normalData == null) { Debug.LogError("Normal Data Missing"); return; }

            int count = 0;
            int maxAttempts = 3000;

            while (count < profile.totalRoomCount - 1 && maxAttempts > 0)
            {
                maxAttempts--;

                // 1. 회전 결정
                int rotIdx = CoreRandom.Range(0, 4);
                int angle = rotIdx * 90;
                int currentW = (rotIdx % 2 == 0) ? normalData.width : normalData.height;
                int currentH = (rotIdx % 2 == 0) ? normalData.height : normalData.width;

                int x = 0, y = 0;
                bool isClusterAttempt = false;

                // 2. 좌표 결정 (뭉침 vs 랜덤)
                if (CoreRandom.Value() < profile.roomClusterChance)
                {
                    isClusterAttempt = true;
                    RoomInstance host = placedRooms[CoreRandom.Range(0, placedRooms.Count)];
                    int direction = CoreRandom.Range(0, 4);
                    int gap = 0; // 딱 붙이기

                    switch (direction)
                    {
                        case 0: x = host.bounds.x + CoreRandom.Range(-(currentW - 2), host.bounds.width - 2); y = host.bounds.y + host.bounds.height + gap; break; // 위
                        case 1: x = host.bounds.x + CoreRandom.Range(-(currentW - 2), host.bounds.width - 2); y = host.bounds.y - currentH - gap; break; // 아래
                        case 2: x = host.bounds.x - currentW - gap; y = host.bounds.y + CoreRandom.Range(-(currentH - 2), host.bounds.height - 2); break; // 왼쪽
                        case 3: x = host.bounds.x + host.bounds.width + gap; y = host.bounds.y + CoreRandom.Range(-(currentH - 2), host.bounds.height - 2); break; // 오른쪽
                    }
                }
                else
                {
                    x = CoreRandom.Range(5, profile.mapWidth - currentW - 5);
                    y = CoreRandom.Range(5, profile.mapHeight - currentH - 5);
                }

                // 3. 유효성 검사
                // 맵 범위 체크
                if (x < 2 || x + currentW > profile.mapWidth - 2 || y < 2 || y + currentH > profile.mapHeight - 2) continue;

                RectInt newRoomRect = new RectInt(x, y, currentW, currentH);

                // 시작 방 안전거리 체크
                if (Vector2.Distance(newRoomRect.center, startCenter) < profile.minDistanceFromStart) continue;

                // 겹침 검사 (뭉침 시도일 땐 딱 붙는 것 허용)
                bool overlap = isClusterAttempt ? IsStrictlyOverlapping(newRoomRect) : IsOverlapping(newRoomRect);

                if (!overlap)
                {
                    RoomInstance newRoom = new RoomInstance();
                    newRoom.data = normalData;
                    newRoom.bounds = newRoomRect;
                    newRoom.currentType = RoomType.Normal;
                    newRoom.rotationAngle = angle; 
                    newRoom.randomTieBreaker = CoreRandom.Value();
                    newRoom.sortPriority = 1; 

                    placedRooms.Add(newRoom);
                    MarkGrid(newRoomRect, 1);
                    count++;
                }
            }
        }

        // --- Step 2: 방 역할 부여 ---
        void AssignRoomTypes()
        {
            // 일반 방 후보군을 무작위로 섞음 (Shuffle)
            List<RoomInstance> candidates = placedRooms.FindAll(r => r.currentType == RoomType.Normal);
            for (int i = 0; i < candidates.Count; i++)
            {
                int randIdx = CoreRandom.Range(i, candidates.Count);
                var temp = candidates[i];
                candidates[i] = candidates[randIdx];
                candidates[randIdx] = temp;
            }

            // 1. [탈출 방] 할당
            int assignedEscape = 0;
            RoomData escData = profile.availableRooms.Find(r => r.type == RoomType.Escape);
            List<RoomInstance> confirmedEscapes = new List<RoomInstance>();

            foreach (var room in candidates)
            {
                if (assignedEscape >= profile.escapeRoomCount) break;
                if (room.currentType != RoomType.Normal) continue;

                // 조건 1: 고립 여부
                if (!profile.allowEscapeRoomCluster && IsTouchingAnyRoom(room)) continue; 

                // 조건 2: 다른 탈출구와의 거리
                bool isTooClose = confirmedEscapes.Any(e => Vector2.Distance(room.bounds.center, e.bounds.center) < profile.minDistanceBetweenEscapes);
                if (isTooClose) continue;

                // 확정
                room.currentType = RoomType.Escape;
                room.sortPriority = 99; // 하이어라키 맨 아래
                if(escData != null) room.data = escData;

                confirmedEscapes.Add(room);
                assignedEscape++;
            }

            // 할당량 미달 시 강제 할당 (조건 무시)
            if (assignedEscape < profile.escapeRoomCount)
            {
                foreach (var room in candidates)
                {
                    if (assignedEscape >= profile.escapeRoomCount) break;
                    if (room.currentType == RoomType.Normal)
                    {
                        room.currentType = RoomType.Escape;
                        room.sortPriority = 99;
                        if(escData != null) room.data = escData;
                        assignedEscape++;
                    }
                }
            }

            // 2. [제단 방] 할당
            int assignedAltar = 0;
            RoomData altarData = profile.availableRooms.Find(r => r.type == RoomType.Altar);
            
            // 탈출구 뽑고 남은 후보들 중에서 다시 뽑음
            candidates = placedRooms.FindAll(r => r.currentType == RoomType.Normal);

            while(assignedAltar < profile.altarRoomCount && candidates.Count > 0)
            {
                int idx = CoreRandom.Range(0, candidates.Count);
                var room = candidates[idx];
                room.currentType = RoomType.Altar;
                room.sortPriority = 1; 
                if(altarData != null) room.data = altarData;

                candidates.RemoveAt(idx);
                assignedAltar++;
            }
        }

        // --- Step 3: 프리팹 생성 ---
        void SpawnRoomPrefabs()
        {
            // 안전장치: 본체 초기화
            foreach(Transform child in transform) Destroy(child.gameObject);
            transform.position = Vector3.zero;
            transform.rotation = Quaternion.identity;
            transform.localScale = Vector3.one;

            Vector2 startPos = new Vector2(placedRooms.Find(r=>r.currentType == RoomType.Start).bounds.x, 0);

            // 정렬: Priority -> 거리 -> 랜덤
            var sortedRooms = placedRooms
                .OrderBy(r => r.sortPriority)
                .ThenBy(r => Vector2.Distance(startPos, new Vector2(r.bounds.x, r.bounds.y)))
                .ThenBy(r => r.randomTieBreaker)
                .ToList();

            int escapeIndex = 1; 

            for (int i = 0; i < sortedRooms.Count; i++)
            {
                RoomInstance room = sortedRooms[i];
                if (room.data.prefab == null) continue;

                // 좌표 계산 (그리드 -> 유니티 좌표)
                int baseX = room.bounds.x * profile.unitSize;
                int baseZ = room.bounds.y * profile.unitSize;

                // 회전 오프셋 (피벗 위치 보정)
                int offsetX = 0;
                int offsetZ = 0;

                switch (room.rotationAngle)
                {
                    case 0:   offsetX = 0; offsetZ = 0; break;
                    case 90:  offsetX = 0; offsetZ = room.data.width * profile.unitSize; break;
                    case 180: offsetX = room.data.width * profile.unitSize; offsetZ = room.data.height * profile.unitSize; break;
                    case 270: offsetX = room.data.height * profile.unitSize; offsetZ = 0; break;
                }

                Vector3 finalPos = new Vector3(baseX + offsetX, 0, baseZ + offsetZ);
                Quaternion rot = Quaternion.Euler(0, room.rotationAngle, 0);

                GameObject go = Instantiate(room.data.prefab, transform); 
                go.transform.localPosition = finalPos;
                go.transform.localRotation = rot;      
                room.spawnedObject = go;

                // 이름 짓기
                if (room.currentType == RoomType.Start)
                {
                    go.name = room.data.prefab.name.Replace("(Clone)", "");
                }
                else if (room.currentType == RoomType.Escape)
                {
                    if (profile.escapeRoomCount > 1) { go.name = $"Escape_{escapeIndex}"; escapeIndex++; }
                    else go.name = "Escape";
                }
                else
                {
                    string prefix = room.currentType.ToString().Substring(0, 1);
                    string suffix = GetPrefabSuffix(room.data.prefab.name);
                    go.name = $"{prefix}_{suffix}_{i}"; 
                }
            }
        }

        // --- Helper Methods ---
        string GetPrefabSuffix(string originalName)
        {
            string[] parts = originalName.Split('_');
            if (parts.Length >= 2) return string.Join("_", parts.Skip(1));
            return originalName;
        }
        
        bool IsOverlapping(RectInt rect)
        {
            foreach (var r in placedRooms)
            {
                RectInt expanded = new RectInt(r.bounds.x - 2, r.bounds.y - 2, r.bounds.width + 4, r.bounds.height + 4);
                if (expanded.Overlaps(rect)) return true;
            }
            return false;
        }

        bool IsStrictlyOverlapping(RectInt rect)
        {
            foreach (var r in placedRooms)
            {
                if (r.bounds.Overlaps(rect)) return true;
            }
            return false;
        }

        bool IsTouchingAnyRoom(RoomInstance room)
        {
            RectInt touchBounds = new RectInt(room.bounds.x - 1, room.bounds.y - 1, room.bounds.width + 2, room.bounds.height + 2);
            foreach(var other in placedRooms)
            {
                if (other == room) continue; 
                if (other.bounds.Overlaps(touchBounds)) return true; 
            }
            return false;
        }

        void MarkGrid(RectInt rect, int value)
        {
            for (int x = rect.x; x < rect.x + rect.width; x++)
            {
                for (int y = rect.y; y < rect.y + rect.height; y++)
                {
                    grid[x, y] = value;
                }
            }
        }
    }
}