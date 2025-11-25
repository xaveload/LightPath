using UnityEngine;
using UnityEngine.AI;
using Unity.AI.Navigation;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using LightPath.Utils;

namespace LightPath.MapGen
{
    public class MapGenerator : MonoBehaviour
    {
        public static MapGenerator Instance;

        [Header("Settings")]
        public MapGenProfile profile;
        
        [Header("Components")]
        public NavMeshSurface navMeshSurface;
        public MapConnectivityTester tester;
        
        [Header("Debug")] 
        public bool autoGenerateOnStart = true;
        public float elapsedTime = 0f;
        
        [Header("Stage Settings")]
        public float currentFloorHeight = 0f; // 외부(StageManager)에서 설정하는 높이 값




        
        // 내부 데이터
        private int[,] grid; // 0:Empty, 1:Room, 2:Corridor, 3:Door
        private List<RoomInstance> placedRooms = new List<RoomInstance>();
        private GameObject mapRoot;
        private Dictionary<Vector2Int, MapTile> tileMap = new Dictionary<Vector2Int, MapTile>();
        
        private List<DoorCluster> plannedClusters = new List<DoorCluster>();
        private HashSet<WallBoundary> createdDoorBoundaries = new HashSet<WallBoundary>();
        private HashSet<Vector2Int> functionalWallPositions = new HashSet<Vector2Int>();
        

        // 그리드 및 보호 구역
        private bool[,] guideGrid;
        private HashSet<Vector2Int> protectedRingTiles = new HashSet<Vector2Int>();

        // --- Data Structures ---
        [System.Serializable]
        public class RoomInstance
        {
            public RoomData data;
            public RectInt bounds;
            public RoomType currentType;
            public GameObject spawnedObject;
            public int rotationAngle;
            public Vector2 Center => bounds.center;
            
            public List<DoorSocket> mySockets = new List<DoorSocket>();
            public bool isConnected = false; 
        }

        [System.Serializable]
        public class DoorSocket
        {
            public Vector2Int gridPos;
            public Vector2Int forward;
            public Vector2Int entryPos;
            [System.NonSerialized] public RoomInstance owner;
            public bool isUsed = false; // 실제 연결에 사용됨
        }

        private class DoorCluster
        {
            public List<Vector2Int> sockets = new List<Vector2Int>();
            public Vector2Int dir;
            public bool isPriority;
            public RoomInstance owner;

        }

        private struct WallBoundary : System.IEquatable<WallBoundary>
        {
            public Vector2Int a, b;
            public WallBoundary(Vector2Int p1, Vector2Int p2)
            {
                if (p1.x < p2.x || (p1.x == p2.x && p1.y < p2.y)) { a = p1; b = p2; } else { a = p2; b = p1; }
            }
            public bool Equals(WallBoundary other) => a == other.a && b == other.b;
            public override int GetHashCode() => a.GetHashCode() ^ b.GetHashCode();
        }

        // A* Node & Heap
        class Node : IHeapItem<Node>
        {
            public Vector2Int pos;
            public Node parent;
            public int gCost;
            public int hCost;
            public int FCost => gCost + hCost;
            public int straightLen; 
            public Vector2Int direction;
            int heapIndex;

            public Node(Vector2Int p, Node pa, int g, int h, int sLen, Vector2Int dir) 
            { 
                pos = p; parent = pa; gCost = g; hCost = h; straightLen = sLen; direction = dir; 
            }
            public int HeapIndex { get => heapIndex; set => heapIndex = value; }
            public int CompareTo(Node other) {
                int compare = FCost.CompareTo(other.FCost);
                if (compare == 0) compare = hCost.CompareTo(other.hCost);
                return -compare;
            }
        }

        public interface IHeapItem<T> : System.IComparable<T> { int HeapIndex { get; set; } }
        public class Heap<T> where T : IHeapItem<T> {
            T[] items; int currentItemCount;
            public Heap(int maxHeapSize) { items = new T[maxHeapSize]; }
            public void Add(T item) { item.HeapIndex = currentItemCount; items[currentItemCount] = item; SortUp(item); currentItemCount++; }
            public T RemoveFirst() { T first = items[0]; currentItemCount--; items[0] = items[currentItemCount]; items[0].HeapIndex = 0; SortDown(items[0]); return first; }
            public int Count => currentItemCount;
            void SortDown(T item) {
                while (true) {
                    int left = item.HeapIndex * 2 + 1; int right = item.HeapIndex * 2 + 2; int swap = 0;
                    if (left < currentItemCount) {
                        swap = left; if (right < currentItemCount && items[left].CompareTo(items[right]) < 0) swap = right;
                        if (item.CompareTo(items[swap]) < 0) Swap(item, items[swap]); else return;
                    } else return;
                }
            }
            void SortUp(T item) {
                int parent = (item.HeapIndex - 1) / 2;
                while (true) {
                    T pItem = items[parent]; if (item.CompareTo(pItem) > 0) Swap(item, pItem); else break;
                    parent = (item.HeapIndex - 1) / 2;
                }
            }
            void Swap(T a, T b) { items[a.HeapIndex] = b; items[b.HeapIndex] = a; int iA = a.HeapIndex; a.HeapIndex = b.HeapIndex; b.HeapIndex = iA; }
        }

        private void Awake() { Instance = this; }
        private void Start() { if (autoGenerateOnStart) GenerateMap(); }

        public void GenerateMap()
        {
            StartCoroutine(GenerateRoutine());
        }

        IEnumerator GenerateRoutine()
        {
            if (!profile) { Debug.LogError("Profile Missing"); yield break; }
            if (CoreRandom.CurrentSeed == 0) CoreRandom.Initialize(1234);

            GameObject stageRoot = GameObject.Find("Stage_Root");
            if (stageRoot == null) 
            {
                stageRoot = new GameObject("Stage_Root");
            }

            grid = new int[profile.mapWidth, profile.mapHeight];
            placedRooms.Clear();
            tileMap.Clear();
            plannedClusters.Clear();
            protectedRingTiles.Clear();
            createdDoorBoundaries.Clear();
            functionalWallPositions.Clear();
            
            mapRoot = new GameObject($"Floor_{currentFloorHeight}");
            mapRoot.transform.SetParent(stageRoot.transform);
            mapRoot.transform.localPosition = Vector3.zero;

            Debug.Log($">>> [MapGen] Start (Seed: {CoreRandom.CurrentSeed})");
            float startTime = Time.realtimeSinceStartup;

            PlaceRooms();
            
            // 1. 모든 소켓 분석
            AnalyzeSockets();
            
            // 2. 가이드 및 링 생성
            GenerateGridGuides();
            if (profile.useOuterLoop)
            {
                ConnectOuterRooms(); // 외곽 연결 (A*)
            }
            GenerateRoomRings();
            yield return null;

            // 3. [핵심] 연결 (Simple Grow)
            yield return StartCoroutine(ConnectSockets());

            // 4. 추가 요소 (샛길, 확장)
            GenerateBranchingCorridors();
            WidenCorridors();

            // 5. 문 계획 (연결된 것 + 확률)
            PlanDoorLocations();

            // 6. 시공
            SpawnRoomPrefabs(mapRoot.transform);
            SpawnCorridors(mapRoot.transform);
            SpawnDoors(mapRoot.transform);
            SpawnCorridorDoors(mapRoot.transform);
            SpawnCeilingLights(mapRoot.transform);

            ConnectAdjacentRooms();

            SortAndRenameRooms();

            if (navMeshSurface != null)
            {
                navMeshSurface.BuildNavMesh();
                Debug.Log(">>> [NavMesh] Baked.");
            }
            if (tester != null) tester.StartTest(placedRooms);

            // 디버그용
            Debug.Log($"[MapGen] Complete ({Time.realtimeSinceStartup - startTime:F2}s)");
            elapsedTime = Time.realtimeSinceStartup - startTime;
        }   

        // -------------------------------------------------------
        // Step 1: Place Rooms
        // -------------------------------------------------------
        void PlaceRooms()
        {
            // Step 0: 예약 공간 (Reserved)
            if (profile.reservedSpaces != null)
            {
                foreach (var space in profile.reservedSpaces)
                {
                    for (int x = space.region.x; x < space.region.xMax; x++)
                        for (int y = space.region.y; y < space.region.yMax; y++)
                            if (IsValidBounds(new RectInt(x, y, 1, 1))) grid[x, y] = 1;

                    int minX = space.region.x - 1, maxX = space.region.xMax;
                    int minY = space.region.y - 1, maxY = space.region.yMax;

                    for (int x = minX; x <= maxX; x++) for (int y = minY; y <= maxY; y++) {
                        if (x == minX || x == maxX || y == minY || y == maxY) {
                            if (IsValidBounds(new RectInt(x, y, 1, 1)) && grid[x, y] == 0) {
                                grid[x, y] = 2; 
                                protectedRingTiles.Add(new Vector2Int(x, y));
                            }
                        }
                    }
                }
            }

            // Step 1: 고정 방 (Fixed)
            if (profile.fixedLayoutRooms != null)
            {
                foreach (var fixedRoom in profile.fixedLayoutRooms)
                {
                    if (fixedRoom.roomData == null) continue;
                    int w = fixedRoom.roomData.width; int h = fixedRoom.roomData.height;
                    bool rotated = (fixedRoom.rotationAngle / 90) % 2 != 0;
                    int finalW = rotated ? h : w; int finalH = rotated ? w : h;
                    RectInt bounds = new RectInt(fixedRoom.position.x, fixedRoom.position.y, finalW, finalH);
                    
                    if (IsValidBounds(bounds)) {
                        AddRoom(fixedRoom.roomData, fixedRoom.position.x, fixedRoom.position.y, fixedRoom.roomData.type, fixedRoom.rotationAngle);
                        
                        int minX = bounds.x - 1, maxX = bounds.xMax;
                        int minY = bounds.y - 1, maxY = bounds.yMax;
                        for (int x = minX; x <= maxX; x++) for (int y = minY; y <= maxY; y++) {
                            if (x == minX || x == maxX || y == minY || y == maxY) {
                                if (IsValidBounds(new RectInt(x, y, 1, 1)) && grid[x, y] == 0) {
                                    grid[x, y] = 2;
                                    protectedRingTiles.Add(new Vector2Int(x, y));
                                }
                            }
                        }
                    }
                }
            }

            // 데이터 참조 미리 가져오기
            var startData = profile.availableRooms.Find(r => r.type == RoomType.Start);
            var escapeData = profile.availableRooms.Find(r => r.type == RoomType.Escape);
            var altarData = profile.availableRooms.Find(r => r.type == RoomType.Altar);
            var normalData = profile.availableRooms.Find(r => r.type == RoomType.Normal);

            // Step 2: 시작 방 배치
            int startX = profile.useFixedStartPos ? profile.fixedStartGridPos.x : (profile.mapWidth - startData.width) / 2;
            int startY = profile.useFixedStartPos ? profile.fixedStartGridPos.y : 2;
            if (!placedRooms.Exists(r => r.currentType == RoomType.Start)) AddRoom(startData, startX, startY, RoomType.Start);
            Vector2 startCenter = placedRooms.Find(r => r.currentType == RoomType.Start).Center;


            // -------------------------------------------------------------
            // Helper: 특정 타입의 방들과 너무 가까운지 확인하는 로직 함수
            // -------------------------------------------------------------
            bool IsTooCloseToType(Vector2 center, RoomType targetType, float minDist)
            {
                foreach (var room in placedRooms)
                {
                    if (room.currentType == targetType)
                    {
                        if (Vector2.Distance(center, room.Center) < minDist) return true;
                    }
                }
                return false;
            }

            // -------------------------------------------------------------
            // Step 3: 탈출구(Escape) 방 배치 (거리 제한 적용)
            // -------------------------------------------------------------
            if (escapeData != null && profile.escapeRoomCount > 0)
            {
                int currentPlaced = 0;
                int targetCount = profile.escapeRoomCount;

                // [3단계 시도] 
                // 0: 원래 거리 제한 준수
                // 1: 거리 제한을 절반으로 완화
                // 2: 거리 제한 무시 (최소한 겹치지만 않게)
                for (int phase = 0; phase < 3; phase++)
                {
                    if (currentPlaced >= targetCount) break; // 목표 달성 시 종료

                    // 단계별 거리 설정
                    float currentMinDist = profile.minDistBetweenEscapes;
                    if (phase == 1) currentMinDist *= 0.5f; // 2단계: 거리 절반
                    else if (phase == 2) currentMinDist = 0f; // 3단계: 거리 무시

                    int attempts = 0;
                    int maxAttempts = (phase == 2) ? 2000 : 1000; // 마지막 단계는 좀 더 많이 시도

                    while (currentPlaced < targetCount && attempts++ < maxAttempts)
                    {
                        int angle = CoreRandom.Range(0, 4) * 90;
                        bool rotated = (angle / 90) % 2 != 0;
                        int w = rotated ? escapeData.height : escapeData.width;
                        int h = rotated ? escapeData.width : escapeData.height;

                        int x = CoreRandom.Range(2, profile.mapWidth - w - 2);
                        int y = CoreRandom.Range(2, profile.mapHeight - h - 2);
                        RectInt rect = new RectInt(x, y, w, h);
                        Vector2 center = rect.center;

                        if (!IsValidBounds(rect)) continue;
                        if (IsOverlappingWithSpacing(rect, 1)) continue;
                        if (Vector2.Distance(center, startCenter) < profile.minDistanceFromStart) continue;

                        // [핵심] 현재 단계의 거리 제한 적용
                        // phase 2(거리 0)일 때는 사실상 IsTooCloseToType 검사를 안 하는 것과 같음
                        if (currentMinDist > 0 && IsTooCloseToType(center, RoomType.Escape, currentMinDist)) continue;

                        AddRoom(escapeData, x, y, RoomType.Escape, angle);
                        currentPlaced++;
                    }
                }

                // 그래도 다 못 채웠으면 경고 로그 출력
                if (currentPlaced < targetCount)
                {
                    Debug.LogWarning($"[MapGen] Escape Room 배치 실패: 공간 부족으로 {targetCount}개 중 {currentPlaced}개만 생성됨.");
                }
                Debug.Log($"[MapGen] Escape Room {currentPlaced} 배치 성공");
            }
            else
            {
                Debug.LogError($"[MapGen] Escape Room 데이터 없음!");
            }

            // -------------------------------------------------------------
            // Step 4: 제단(Altar) 방 배치 (거리 제한 적용)
            // -------------------------------------------------------------
            if (altarData != null && profile.altarRoomCount > 0)
            {
                int currentPlaced = 0;
                int targetCount = profile.altarRoomCount;

                // [3단계 시도] 
                // 0: 원래 거리 제한 준수
                // 1: 거리 제한을 절반으로 완화
                // 2: 거리 제한 무시 (최소한 겹치지만 않게)
                for (int phase = 0; phase < 3; phase++)
                {
                    if (currentPlaced >= targetCount) break; // 목표 달성 시 종료

                    // 단계별 거리 설정
                    float currentMinDist = profile.minDistBetweenAltars;
                    if (phase == 1) currentMinDist *= 0.5f; // 2단계: 거리 절반
                    else if (phase == 2) currentMinDist = 0f; // 3단계: 거리 무시

                    int attempts = 0;
                    int maxAttempts = (phase == 2) ? 2000 : 1000; // 마지막 단계는 좀 더 많이 시도

                    while (currentPlaced < targetCount && attempts++ < maxAttempts)
                    {
                        int angle = CoreRandom.Range(0, 4) * 90;
                        bool rotated = (angle / 90) % 2 != 0;
                        int w = rotated ? altarData.height : altarData.width;
                        int h = rotated ? altarData.width : altarData.height;

                        int x = CoreRandom.Range(2, profile.mapWidth - w - 2);
                        int y = CoreRandom.Range(2, profile.mapHeight - h - 2);
                        RectInt rect = new RectInt(x, y, w, h);
                        Vector2 center = rect.center;

                        if (!IsValidBounds(rect)) continue;
                        if (IsOverlappingWithSpacing(rect, 1)) continue;
                        if (Vector2.Distance(center, startCenter) < profile.minDistanceFromStart) continue;

                        // [핵심] 현재 단계의 거리 제한 적용
                        // phase 2(거리 0)일 때는 사실상 IsTooCloseToType 검사를 안 하는 것과 같음
                        if (currentMinDist > 0 && IsTooCloseToType(center, RoomType.Altar, currentMinDist)) continue;

                        AddRoom(altarData, x, y, RoomType.Altar, angle);
                        currentPlaced++;
                    }
                }

                // 그래도 다 못 채웠으면 경고 로그 출력
                if (currentPlaced < targetCount)
                {
                    Debug.LogWarning($"[MapGen] Altar Room 배치 실패: 공간 부족으로 {targetCount}개 중 {currentPlaced}개만 생성됨.");
                }
                Debug.Log($"[MapGen] Altar Room {currentPlaced} 배치 성공");
            }
            else
            {
                Debug.LogError($"[MapGen] Altar Room 데이터 없음!");
            }


            // -------------------------------------------------------------
            // Step 5: 나머지 일반 방 채우기 (기존 로직 - Cluster 등 적용)
            // -------------------------------------------------------------
            int count = placedRooms.Count; // 이미 배치된 방 개수 포함
            int loopAttempts = 0;

            while (count < profile.totalRoomCount && loopAttempts++ < 5000)
            {
                int angle = CoreRandom.Range(0, 4) * 90; bool rotated = (angle / 90) % 2 != 0;
                int w = rotated ? normalData.height : normalData.width; int h = rotated ? normalData.width : normalData.height;
                int x, y;

                // 클러스터링 로직은 일반 방 배치 때 주로 사용
                var potentialHosts = placedRooms.ToList(); // 모든 방을 호스트로 삼음
                bool canCluster = potentialHosts.Count > 0 && CoreRandom.Value() < profile.roomClusterChance;
                
                if (canCluster) {
                    var host = potentialHosts[CoreRandom.Range(0, potentialHosts.Count)];
                    Vector2Int[] dirs = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
                    Vector2Int dir = dirs[CoreRandom.Range(0, 4)];
                    
                    if (dir == Vector2Int.right) x = host.bounds.xMax; 
                    else if (dir == Vector2Int.left) x = host.bounds.x - w; 
                    else x = CoreRandom.Range(host.bounds.x - w + 1, host.bounds.xMax - 1);
                    
                    if (dir == Vector2Int.up) y = host.bounds.yMax; 
                    else if (dir == Vector2Int.down) y = host.bounds.y - h; 
                    else y = CoreRandom.Range(host.bounds.y - h + 1, host.bounds.yMax - 1);
                } 
                else { 
                    x = CoreRandom.Range(2, profile.mapWidth - w - 2); 
                    y = CoreRandom.Range(2, profile.mapHeight - h - 2); 
                }

                RectInt rect = new RectInt(x, y, w, h);
                
                if (!IsValidBounds(rect)) continue;
                if (!canCluster && Vector2.Distance(rect.center, startCenter) < profile.minDistanceFromStart) continue;
                
                if (canCluster) { if (IsStrictlyOverlapping(rect)) continue; } 
                else { if (IsOverlappingWithSpacing(rect, 1)) continue; }
                
                AddRoom(normalData, x, y, RoomType.Normal, angle); 
                count++;
            }
        }

        // [외부 제어용 함수] StageManager가 호출할 수 있게!
        public void SetFixedConfig(List<FixedRoomEntry> fixedRooms, List<ReservedSpaceEntry> reservedSpaces)
        {
            if (profile == null) return;
            // 프로필 원본을 수정하면 안 되므로, 런타임 복제본을 쓰거나 임시 변수에 저장해야 함.
            // 여기서는 프로필의 리스트를 덮어쓰는 예시 (주의: 에디터 모드에서는 에셋이 변경됨)
            profile.fixedLayoutRooms = new List<FixedRoomEntry>(fixedRooms);
            profile.reservedSpaces = new List<ReservedSpaceEntry>(reservedSpaces);
        }
        void AddRoom(RoomData data, int x, int y, RoomType type, int angle = 0) {
            bool rotated = (angle / 90) % 2 != 0;
            var room = new RoomInstance { data = data, bounds = new RectInt(x, y, rotated ? data.height : data.width, rotated ? data.width : data.height), currentType = type, rotationAngle = angle };
            placedRooms.Add(room); MarkGrid(room.bounds, 1);
        }

        // -------------------------------------------------------
        // Step 2: Analyze Sockets
        // -------------------------------------------------------
        void AnalyzeSockets() {
            foreach (var room in placedRooms) {
                if (room.data == null) continue;
                if (room.data.possibleDoorSpots == null || room.data.doorDirections == null) continue;
                int count = Mathf.Min(room.data.possibleDoorSpots.Count, room.data.doorDirections.Count);
                for (int i = 0; i < count; i++) {
                    Vector2Int gPos = TransformPoint(room.data.possibleDoorSpots[i], room);
                    Vector2Int dir = RotateVector(room.data.doorDirections[i], room.rotationAngle);
                    Vector2Int entry = gPos + dir; 
                    if (!IsValidBounds(new RectInt(entry.x, entry.y, 1, 1))) continue;
                    if (grid[entry.x, entry.y] == 1) continue; 
                    DoorSocket socket = new DoorSocket { gridPos = gPos, forward = dir, entryPos = entry, owner = room };
                    room.mySockets.Add(socket); 
                }
            }
        }


        // -------------------------------------------------------
        // Step 3: Generate Room Rings
        // -------------------------------------------------------
        void GenerateRoomRings() {
            if (profile.roomRingChance <= 0) return;
            foreach (var room in placedRooms) {
                if (CoreRandom.Value() > profile.roomRingChance) continue;
                RectInt b = new RectInt(room.bounds.x - 1, room.bounds.y - 1, room.bounds.width + 2, room.bounds.height + 2);
                for (int x = b.x; x < b.xMax; x++) for (int y = b.y; y < b.yMax; y++) {
                    if (x == b.x || x == b.xMax - 1 || y == b.y || y == b.yMax - 1) {
                        if (IsValidBounds(new RectInt(x, y, 1, 1)) && grid[x, y] == 0) grid[x, y] = 2; 
                    }
                }
            }
        }

        // -------------------------------------------------------
        // Step 4: Connect Sockets (A*)
        // -------------------------------------------------------
        IEnumerator ConnectSockets()
        {
            List<RoomInstance> connected = new List<RoomInstance>();
            List<RoomInstance> unconnected = new List<RoomInstance>(placedRooms);

            var startRoom = placedRooms.Find(r => r.currentType == RoomType.Start) ?? placedRooms[0];
            connected.Add(startRoom); unconnected.Remove(startRoom); startRoom.isConnected = true;

            int safety = 0;
            RoomInstance lastConnectedRoom = startRoom; // Subway용
            Vector2 mapCenter = startRoom.Center; // AntHill용

            // [헬퍼] 정규분포 랜덤
            float GetGaussian(float mean, float stdDev)
            {
                float u1 = 1.0f - CoreRandom.Value(); 
                float u2 = 1.0f - CoreRandom.Value();
                float randStdNormal = Mathf.Sqrt(-2.0f * Mathf.Log(u1)) * Mathf.Sin(2.0f * Mathf.PI * u2); 
                return mean + stdDev * randStdNormal;
            }

            while (unconnected.Count > 0)
            {
                if (safety++ > 5000) { Debug.LogError(">>> [Connect] Loop Error"); break; }

                // ------------------------------------------------------------------
                // 1. [공통] 이번 턴의 '목표 거리' 산출
                //    (모든 레이아웃이 이 거리 공식을 따름 -> 변수 3개 모두 활용)
                // ------------------------------------------------------------------
                float maxMapDist = Mathf.Sqrt(profile.mapWidth * profile.mapWidth + profile.mapHeight * profile.mapHeight);
                
                float targetRatio = GetGaussian(profile.longPathProb, profile.distanceVariance);
                targetRatio = Mathf.Clamp01(targetRatio);
                float targetDist = Mathf.Lerp(profile.minSocketDistance, maxMapDist, targetRatio);


                // ------------------------------------------------------------------
                // 2. [개성] 출발 방(Source) 선정
                // ------------------------------------------------------------------
                RoomInstance sourceRoom = null;
                bool isSubwayMode = (profile.layoutType == MapLayoutType.Subway);

                switch (profile.layoutType)
                {
                    case MapLayoutType.Subway:
                        sourceRoom = lastConnectedRoom; 
                        break;

                    case MapLayoutType.AntHill:
                        // 연결된 방 중 '중앙'에 가장 가까운 상위권 방들 중 하나 선택 (너무 똑같은 방만 걸리면 안 되니)
                        // 여기서는 간단히 '가장 가까운 방' 선택
                        float minCenterDist = float.MaxValue;
                        foreach(var r in connected) {
                            float d = Vector2.Distance(r.Center, mapCenter);
                            if(d < minCenterDist) { minCenterDist = d; sourceRoom = r; }
                        }
                        break;

                    case MapLayoutType.SpiderWeb:
                    default:
                        sourceRoom = connected[CoreRandom.Range(0, connected.Count)];
                        break;
                }


                // ------------------------------------------------------------------
                // 3. [탐색] 최적의 타겟 찾기 (목표 거리와 가장 가까운 방)
                // ------------------------------------------------------------------
                RoomInstance targetRoom = null;
                float bestScore = float.MaxValue; // |실제거리 - 목표거리| 차이가 작을수록 좋음

                // 타겟 찾기 시도
                foreach(var ur in unconnected) {
                    float dist = Vector2.Distance(sourceRoom.Center, ur.Center);
                    
                    // [공통 제약] 최소 거리보다 가까우면 후보 탈락
                    if (dist < profile.minSocketDistance) continue;

                    // 점수 계산: 목표 거리(targetDist)와의 오차
                    float score = Mathf.Abs(dist - targetDist);

                    if (score < bestScore) {
                        bestScore = score;
                        targetRoom = ur;
                    }
                }

                // [예외 처리] Subway 모드인데 막다른 길이라 타겟을 못 찾았다면? -> 랜덤 모드로 1회 전환
                if (targetRoom == null && isSubwayMode)
                {
                    // "지하철 공사가 막혔으니, 아무 역에서나 지선(Branch)을 뚫자"
                    sourceRoom = connected[CoreRandom.Range(0, connected.Count)];
                    
                    // 다시 검색
                    bestScore = float.MaxValue;
                    foreach(var ur in unconnected) {
                        float dist = Vector2.Distance(sourceRoom.Center, ur.Center);
                        if (dist < profile.minSocketDistance) continue;
                        float score = Mathf.Abs(dist - targetDist);
                        if (score < bestScore) { bestScore = score; targetRoom = ur; }
                    }
                }

                // ------------------------------------------------------------------
                // 4. [연결] A* 실행
                // ------------------------------------------------------------------
                DoorSocket bestSource = null, bestTarget = null;
                
                if (targetRoom != null) {
                    // 소켓 단위 정밀 거리 계산 (이것도 targetDist에 가까운 걸로)
                    float bestSocketScore = float.MaxValue;
                    foreach(var sA in sourceRoom.mySockets) {
                        foreach(var sB in targetRoom.mySockets) {
                            float d = Vector2Int.Distance(sA.entryPos, sB.entryPos);
                            if (d < profile.minSocketDistance) continue;
                            
                            float score = Mathf.Abs(d - targetDist);
                            if (score < bestSocketScore) { 
                                bestSocketScore = score; 
                                bestSource = sA; 
                                bestTarget = sB; 
                            }
                        }
                    }
                }

                bool success = false;
                if (bestSource != null && bestTarget != null)
                {
                    var path = FindPathAStar(bestSource.entryPos, bestTarget.entryPos);
                    if (path != null)
                    {
                        foreach (var p in path) if (grid[p.x, p.y] == 0) grid[p.x, p.y] = 2;
                        
                        // [필수] 문 자리 확정 -> PlanDoorLocations가 감지함
                        MarkSocketAsMust(bestSource);
                        MarkSocketAsMust(bestTarget);
                        
                        targetRoom.isConnected = true;
                        connected.Add(targetRoom);
                        unconnected.Remove(targetRoom);
                        
                        lastConnectedRoom = targetRoom; // 지하철 꼬리 갱신
                        success = true;
                    }
                }

                // 실패 시 고립 인정하고 넘기기 (무한루프 방지)
                if (!success && unconnected.Count > 0) {
                    var giveUp = (targetRoom != null) ? targetRoom : unconnected[0];
                    // 일단 연결된 척 처리해서 루프 탈출 (나중에 구조됨)
                    connected.Add(giveUp); 
                    unconnected.Remove(giveUp); 
                }
                
                if(unconnected.Count % 5 == 0) yield return null;
            }

            // [추가] Extra Loops 생성 (기존 로직 유지)
            GenerateExtraLoops();
        }

        // [누락 방지] 소켓 마킹 헬퍼
        void MarkSocketAsMust(DoorSocket s) 
        {
            s.isUsed = true;
            // mstUsedSockets.Add(s); // HashSet이 있다면 추가
            grid[s.gridPos.x, s.gridPos.y] = 3; 
        }

        // [누락 방지] Extra Loops
        void GenerateExtraLoops()
        {
            if (profile.extraConnectionChance <= 0) return;
            
            int count = Mathf.FloorToInt(placedRooms.Count * profile.extraConnectionChance);
            
            for (int i = 0; i < count; i++)
            {
                RoomInstance rA = placedRooms[CoreRandom.Range(0, placedRooms.Count)];
                RoomInstance rB = placedRooms[CoreRandom.Range(0, placedRooms.Count)];
                if (rA == rB) continue;

                // 안 쓴 소켓 재활용 가능하도록
                DoorSocket sA = rA.mySockets.Count > 0 ? rA.mySockets[CoreRandom.Range(0, rA.mySockets.Count)] : null;
                DoorSocket sB = rB.mySockets.Count > 0 ? rB.mySockets[CoreRandom.Range(0, rB.mySockets.Count)] : null;
                
                if (sA != null && sB != null && Vector2Int.Distance(sA.entryPos, sB.entryPos) > 15)
                {
                    var path = FindPathAStar(sA.entryPos, sB.entryPos, true); // Force New Path
                    if (path != null)
                    {
                        foreach (var p in path) 
                            if (grid[p.x, p.y] == 0) grid[p.x, p.y] = 2;
                            
                        // [중요] 추가 연결된 소켓도 필수로 마킹
                        MarkSocketAsMust(sA); 
                        MarkSocketAsMust(sB);
                    }
                }
            }
        }

        List<Vector2Int> FindPathAStar(Vector2Int start, Vector2Int end, bool forceNew = false)
        {
            bool[,] closed = new bool[profile.mapWidth, profile.mapHeight];
            int[,] minG = new int[profile.mapWidth, profile.mapHeight];
            for(int x=0;x<profile.mapWidth;x++) for(int y=0;y<profile.mapHeight;y++) minG[x,y]=int.MaxValue;

            Heap<Node> open = new Heap<Node>(profile.mapWidth * profile.mapHeight * 2);
            minG[start.x, start.y] = 0;
            open.Add(new Node(start, null, 0, GetManhattanDistance(start, end), 0, Vector2Int.zero));

            int safety = 30000;
            while (open.Count > 0)
            {
                if (safety-- <= 0) return null;
                Node curr = open.RemoveFirst();
                closed[curr.pos.x, curr.pos.y] = true;

                if (curr.pos == end) return RetracePath(curr);

                Vector2Int[] dirs = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
                foreach (var d in dirs)
                {
                    Vector2Int nPos = curr.pos + d;
                    if (!IsValidBounds(new RectInt(nPos.x, nPos.y, 1, 1))) continue;
                    if (closed[nPos.x, nPos.y]) continue;

                    int cell = grid[nPos.x, nPos.y];
                    // 방(1)은 통과 불가, 문(3)은 통과 가능! (이미 뚫린 문을 경유할 수 있게)
                    if (nPos != end && cell == 1) continue; 

                    int newStraight = (d == curr.direction) ? curr.straightLen + 1 : 1;
                    if (newStraight > profile.pathStraightLimit && nPos != end) continue;

                    int baseCost = 10;
                    if (!forceNew && cell == 2) baseCost = profile.existingPathCost;
                    if (IsOnGuideLine(nPos)) baseCost = Mathf.Max(1, baseCost - profile.guideLineDiscount);
                    
                    // 기존 문(3)을 지나가면 비용 대폭 할인 (허브 역할)
                    if (cell == 3) baseCost = 1; 

                    int newCost = curr.gCost + baseCost;
                    if (newCost < minG[nPos.x, nPos.y]) {
                        minG[nPos.x, nPos.y] = newCost;
                        open.Add(new Node(nPos, curr, newCost, GetManhattanDistance(nPos, end), newStraight, d));
                    }
                }
            }
            return null;
        }

        List<Vector2Int> RetracePath(Node endNode) { List<Vector2Int> path = new List<Vector2Int>(); Node curr = endNode; while (curr != null) { path.Add(curr.pos); curr = curr.parent; } path.Reverse(); return path; }

        // [누락되었던 함수] 경로 역추적
        void PlanDoorLocations()
        {
            plannedClusters.Clear();
            GameObject floorPrefab = GetRandomFloorPrefab();
            Transform corridorRoot = mapRoot.transform.Find("Corridors") ?? mapRoot.transform;

            foreach (var room in placedRooms) {
                if (room.data == null) continue;
                if (room.data.possibleDoorSpots == null || room.data.doorDirections == null) continue;

                Dictionary<Vector2Int, Vector2Int> roomSockets = new Dictionary<Vector2Int, Vector2Int>();
                int count = Mathf.Min(room.data.possibleDoorSpots.Count, room.data.doorDirections.Count);
                for (int i = 0; i < count; i++) {
                    Vector2Int gPos = TransformPoint(room.data.possibleDoorSpots[i], room);
                    Vector2Int dir = RotateVector(room.data.doorDirections[i], room.rotationAngle);
                    if (!roomSockets.ContainsKey(gPos)) roomSockets.Add(gPos, dir);
                }
                List<DoorCluster> clusters = new List<DoorCluster>();
                HashSet<Vector2Int> visited = new HashSet<Vector2Int>();
                foreach (var kvp in roomSockets) {
                    if (visited.Contains(kvp.Key)) continue;
                    DoorCluster cluster = new DoorCluster { dir = kvp.Value, owner = room };
                    Queue<Vector2Int> q = new Queue<Vector2Int>(); q.Enqueue(kvp.Key); visited.Add(kvp.Key);
                    while (q.Count > 0) {
                        Vector2Int curr = q.Dequeue(); cluster.sockets.Add(curr);
                        Vector2Int[] nbs = { curr + Vector2Int.right, curr - Vector2Int.right, curr + Vector2Int.up, curr - Vector2Int.up };
                        foreach (var n in nbs) if (roomSockets.TryGetValue(n, out Vector2Int nd) && nd == cluster.dir && !visited.Contains(n)) { visited.Add(n); q.Enqueue(n); }
                    }
                    clusters.Add(cluster);
                }

            
                List<DoorCluster> optionList = new List<DoorCluster>();
                List<DoorCluster> roomLinkList = new List<DoorCluster>();
                List<DoorCluster> rescueList = new List<DoorCluster>();

                foreach (var c in clusters) {
                    bool isCorridorTouch = false;
                    bool isRoomLink = false;
                    foreach(var s in c.sockets) {
                        Vector2Int front = s + c.dir;
                        if(!IsValidBounds(new RectInt(front.x, front.y, 1, 1))) continue;
                        // [수정] Grid 3 체크 삭제. 오직 Grid 2(복도) 접촉만 확인
                        if (grid[front.x, front.y] == 2 || HasNeighborCorridor(front)) isCorridorTouch = true;
                        else if (grid[front.x, front.y] == 1 && CheckRoomConnectionStrict(s, front, -c.dir)) isRoomLink = true;
                    }
                    // [수정] Must 리스트 삭제. 모두 Option으로 분류
                    if (isRoomLink) roomLinkList.Add(c); // 방 연결은 별도
                    else if (isCorridorTouch) optionList.Add(c); // 복도 연결은 옵션
                    else rescueList.Add(c); // 고립
                }

                List<DoorCluster> finalPicks = new List<DoorCluster>();

                // 확률 체크
                int targetCount = 1;
                if (profile.extraDoorCountChances != null)
                {
                    for (int i = 0; i < profile.extraDoorCountChances.Count; i++)
                    {
                        // 이번 단계 확률 통과하면 목표 개수 증가
                        if (CoreRandom.Value() < profile.extraDoorCountChances[i]) 
                        {
                            targetCount = 2 + i;
                        }
                        else 
                        {
                            break; // 실패하면 즉시 멈춤 (상위 단계 도전 불가)
                        }
                    }
                }

                // 이미 연결된 문이 목표치보다 많으면 더 안 뽑음 (단, MST가 뚫은 건 줄이지 않음)
                int needed = targetCount - finalPicks.Count;
                
                // 예외: 필수가 0개면 옵션/고립에서 1개 강제 (고립 방지)
                if (finalPicks.Count == 0) {
                    if (optionList.Count > 0) { optionList = optionList.OrderBy(x => CoreRandom.Value()).ToList(); finalPicks.Add(optionList[0]); optionList.RemoveAt(0); needed--; }
                    else if (rescueList.Count > 0) {
                        var rescue = rescueList[CoreRandom.Range(0, rescueList.Count)];
                        rescue.isPriority = true; finalPicks.Add(rescue);
                        foreach(var s in rescue.sockets) ConnectToNearestCorridor(s + rescue.dir, floorPrefab, corridorRoot);
                        needed--;
                    }
                }

                if (needed > 0 && optionList.Count > 0) {
                    optionList = optionList.OrderBy(x => CoreRandom.Value()).ToList();
                    for(int i=0; i<Mathf.Min(targetCount, optionList.Count); i++) {
                        finalPicks.Add(optionList[i]);}
                    }

                if (finalPicks.Count == 0 && rescueList.Count > 0) {
                    var rescue = rescueList[CoreRandom.Range(0, rescueList.Count)];
                    finalPicks.Add(rescue);
                    foreach(var s in rescue.sockets) ConnectToNearestCorridor(s + rescue.dir, floorPrefab, corridorRoot);
                }

                foreach(var c in finalPicks) { c.isPriority = true; plannedClusters.Add(c); }

                // [Phase 2] 방-방 연결 (메인 문 로직과 별개로 1개 추가 가능)
                foreach(var link in roomLinkList) {
                    if (!finalPicks.Contains(link)) {
                        link.isPriority = true; plannedClusters.Add(link);
                        break; // 방 하나당 연결 통로 1개만 허용
                    }
                }
            }

            foreach(var c in plannedClusters) foreach(var s in c.sockets) {
                Vector2Int f = s + c.dir; EnsureCorridorFloorExists(f, floorPrefab, corridorRoot);
            }
        }
        
        int GetManhattanDistance(Vector2Int a, Vector2Int b) => Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);


        void GenerateGridGuides()
        {   
            // [수정] 텍스처 가이드가 활성화되면 바로 텍스처 처리로 넘어가도록 로직 변경
            if (profile.useTextureGuide && profile.guideTexture != null)
            {
                try
                {
                    Texture2D tex = profile.guideTexture;
                    guideGrid = new bool[profile.mapWidth, profile.mapHeight];
                    guideGrid.Initialize();

                    for (int x = 0; x < profile.mapWidth; x++)
                    {
                        for (int y = 0; y < profile.mapHeight; y++)
                        {
                            float u = (float)x / profile.mapWidth;
                            float v = (float)y / profile.mapHeight;

                            Color color = tex.GetPixelBilinear(u, v);

                            if (color.a >= profile.guideAlphaThreshold)
                            {
                                guideGrid[x, y] = true;
                            }
                        }
                    }
                    Debug.Log("[Guide] Generated guides from Texture.");
                    return;
                }
                catch (UnityException e)
                {
                    Debug.LogError($"[Guide] 텍스처 읽기 실패! Read/Write 옵션을 켜주세요. {e.Message}");
                    // 실패 시 프로시저럴 모드로 폴백
                }
            }

            // -------------------------------------------------------
            // 모드 B: 프로시저럴 그리드 (기존 로직)
            // -------------------------------------------------------
            if (profile.gridDivisionX != 0 && profile.gridDivisionY != 0)
            {
                guideGrid = new bool[profile.mapWidth, profile.mapHeight];
                guideGrid.Initialize();

                List<int> linesX = new List<int>();
                List<int> linesY = new List<int>();

                // 1. 가이드 라인 위치 계산
                if (profile.gridDivisionX > 0) { int stepX = profile.mapWidth / (profile.gridDivisionX + 1); for (int i = 1; i <= profile.gridDivisionX; i++) linesX.Add(Mathf.Clamp(i * stepX + CoreRandom.Range(-profile.gridOffsetRange, profile.gridOffsetRange + 1), 5, profile.mapWidth - 6)); }
                if (profile.gridDivisionY > 0) { int stepY = profile.mapHeight / (profile.gridDivisionY + 1); for (int i = 1; i <= profile.gridDivisionY; i++) linesY.Add(Mathf.Clamp(i * stepY + CoreRandom.Range(-profile.gridOffsetRange, profile.gridOffsetRange + 1), 5, profile.mapHeight - 6)); }
                
                linesX.Add(3); linesX.Add(profile.mapWidth - 4);
                linesY.Add(3); linesY.Add(profile.mapHeight - 4);

                // 2. [수정됨] 배열에 마킹 (중괄호 {} 추가)
                foreach (int gx in linesX)
                {
                    for (int y = 0; y < profile.mapHeight; y++) 
                    {
                        // y가 이제 루프 범위 안에서 정의됨
                        if (IsValidBounds(new RectInt(gx, y, 1, 1))) guideGrid[gx, y] = true;
                        if (IsValidBounds(new RectInt(gx-1, y, 1, 1))) guideGrid[gx-1, y] = true;
                        if (IsValidBounds(new RectInt(gx+1, y, 1, 1))) guideGrid[gx+1, y] = true;
                    }
                }
                foreach (int gy in linesY)
                {
                    for (int x = 0; x < profile.mapWidth; x++) 
                    {
                        // x가 이제 루프 범위 안에서 정의됨
                        if (IsValidBounds(new RectInt(x, gy, 1, 1))) guideGrid[x, gy] = true;
                        if (IsValidBounds(new RectInt(x, gy-1, 1, 1))) guideGrid[x, gy-1] = true;
                        if (IsValidBounds(new RectInt(x, gy+1, 1, 1))) guideGrid[x, gy+1] = true;
                    }
                }

                Debug.Log($"[Guide] Generated Procedural Grid ({linesX.Count}x{linesY.Count}).");
            }
            else
            {
                Debug.Log($"[Guide] No Guide Square Grid Generated.");
            }
        }

        void ConnectOuterRooms()
        {
            if (profile.useOuterLoop == true)
            {
                    Vector2 center = new Vector2(profile.mapWidth / 2f, profile.mapHeight / 2f);
                List<RoomInstance> outerRooms = new List<RoomInstance>();
                
                int sectors = 12; float angleStep = 360f / sectors;
                for (int i = 0; i < sectors; i++) {
                    float startAngle = i * angleStep; float endAngle = (i + 1) * angleStep;
                    RoomInstance bestRoom = null; float maxDist = -1f;
                    foreach (var room in placedRooms) {
                        Vector2 dir = room.Center - center; float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
                        if (angle < 0) angle += 360f;
                        if (angle >= startAngle && angle < endAngle) {
                            float dist = dir.sqrMagnitude;
                            if (dist > maxDist && dist > (20 * 20)) { maxDist = dist; bestRoom = room; }
                        }
                    }
                    if (bestRoom != null && !outerRooms.Contains(bestRoom)) outerRooms.Add(bestRoom);
                }
                
                outerRooms = outerRooms.OrderBy(r => { Vector2 dir = r.Center - center; return Mathf.Atan2(dir.y, dir.x); }).ToList();

                for (int i = 0; i < outerRooms.Count; i++)
                {
                    RoomInstance roomA = outerRooms[i];
                    RoomInstance roomB = outerRooms[(i + 1) % outerRooms.Count];
                    DoorSocket bestA = null, bestB = null; float minDist = float.MaxValue;
                    foreach (var sA in roomA.mySockets) foreach (var sB in roomB.mySockets) {
                        float d = Vector2Int.Distance(sA.entryPos, sB.entryPos);
                        if (d < minDist) { minDist = d; bestA = sA; bestB = sB; }
                    }
                    if (bestA != null && bestB != null) {
                        // [중요] forceNewPath = true로 해서 기존 복도 무시하고 외곽선 뚫기
                        var path = FindPathAStar(bestA.entryPos, bestB.entryPos, true);
                        if (path != null) {
                            foreach (var p in path) if (grid[p.x, p.y] == 0) grid[p.x, p.y] = 2;
                            bestA.isUsed = true; bestB.isUsed = true;
                            grid[bestA.gridPos.x, bestA.gridPos.y] = 3; grid[bestB.gridPos.x, bestB.gridPos.y] = 3;
                        }
                    }
                }
            }
        }


        
        // [헬퍼] 해당 위치가 가이드 라인 위인지 체크 (약간의 범위 허용)
        bool IsOnGuideLine(Vector2Int pos)
        {
            if (guideGrid == null) return false;
            // 배열 값만 확인하면 되므로 O(1) 속도
            return guideGrid[pos.x, pos.y];
        }

        // -------------------------------------------------------
        // Step 5: Plan Doors
        // -------------------------------------------------------
        
        void WidenCorridors()
        {
            if (profile.wideCorridorChance <= 0) return;
            
            List<Vector2Int> toWiden = new List<Vector2Int>();

            // 1. 가로(Horizontal) 방향 스캔
            for (int y = 1; y < profile.mapHeight - 1; y++)
            {
                for (int x = 1; x < profile.mapWidth - 1; x++)
                {
                    // 복도(2)의 시작점인가? (왼쪽이 복도가 아님)
                    if (grid[x, y] == 2 && grid[x - 1, y] != 2)
                    {

                        // 구간 길이 측정
                        int length = 0;
                        while (x + length < profile.mapWidth - 1 && grid[x + length, y] == 2)
                        {
                            length++;
                        }

                        // 확률 체크 (구간 전체에 대해 한 번만!)
                        if (CoreRandom.Value() < profile.wideCorridorChance)
                        {
                            // 확장 방향 결정 (위 or 아래) - 빈 공간이 많은 쪽 선호하면 좋겠지만 일단 랜덤/고정
                            // 여기서는 '아래쪽(y-1)'으로 확장 시도
                            // (단, 확장할 곳이 0(빈곳)이어야 하고, 방(1)이나 문(3)을 덮어쓰면 안 됨)
                            
                            bool canExpandDown = true;
                            for (int k = 0; k < length; k++)
                            {
                                int targetType = grid[x + k, y - 1];
                                if (targetType == 1 || targetType == 3) { canExpandDown = false; break; }
                            }

                            if (canExpandDown)
                            {
                                for (int k = 0; k < length; k++) toWiden.Add(new Vector2Int(x + k, y - 1));
                            }
                            else
                            {
                                // 아래가 안 되면 위쪽(y+1) 시도
                                bool canExpandUp = true;
                                for (int k = 0; k < length; k++)
                                {
                                    int targetType = grid[x + k, y + 1];
                                    if (targetType == 1 || targetType == 3) { canExpandUp = false; break; }
                                }
                                if (canExpandUp)
                                {
                                    for (int k = 0; k < length; k++) toWiden.Add(new Vector2Int(x + k, y + 1));
                                }
                            }
                        }
                        
                        // 탐색 인덱스 점프 (이미 확인한 구간 건너뛰기)
                        x += length; 
                    }
                }
            }

            // 2. 세로(Vertical) 방향 스캔
            for (int x = 1; x < profile.mapWidth - 1; x++)
            {
                for (int y = 1; y < profile.mapHeight - 1; y++)
                {
                    
                    // 복도(2)의 시작점인가? (아래쪽이 복도가 아님)
                    if (grid[x, y] == 2 && grid[x, y - 1] != 2)
                    {
                        int length = 0;
                        while (y + length < profile.mapHeight - 1 && grid[x, y + length] == 2)
                        {
                            length++;
                        }

                        if (CoreRandom.Value() < profile.wideCorridorChance)
                        {
                            // 왼쪽(x-1) 확장 시도
                            bool canExpandLeft = true;
                            for (int k = 0; k < length; k++)
                            {
                                int targetType = grid[x - 1, y + k];
                                if (targetType == 1 || targetType == 3) { canExpandLeft = false; break; }
                            }

                            if (canExpandLeft)
                            {
                                for (int k = 0; k < length; k++) toWiden.Add(new Vector2Int(x - 1, y + k));
                            }
                            else
                            {
                                // 오른쪽(x+1) 확장 시도
                                bool canExpandRight = true;
                                for (int k = 0; k < length; k++)
                                {
                                    int targetType = grid[x + 1, y + k];
                                    if (targetType == 1 || targetType == 3) { canExpandRight = false; break; }
                                }
                                if (canExpandRight)
                                {
                                    for (int k = 0; k < length; k++) toWiden.Add(new Vector2Int(x + 1, y + k));
                                }
                            }
                        }
                        y += length;
                    }
                }
            }

            // 3. 일괄 적용 (중복 방지 및 최종 확인)
            foreach (var pos in toWiden)
            {
                
                // 원래 빈 공간(0)이었던 곳만 복도(2)로 바꿈 (방이나 문 덮어쓰기 방지)
                if (grid[pos.x, pos.y] == 0)
                {
                    grid[pos.x, pos.y] = 2;
                }
            }
            
            Debug.Log($"[Widen] Continuous expansion applied to {toWiden.Count} tiles.");
        }
        
        void ConnectToNearestCorridor(Vector2Int start, GameObject floorPrefab, Transform parent)
        {
            Vector2Int bestTarget = start;
            float minDst = float.MaxValue;
            bool found = false;
            int range = 50; // 탐색 범위 넉넉하게

            for (int x = start.x - range; x <= start.x + range; x++)
            {
                for (int y = start.y - range; y <= start.y + range; y++)
                {
                    if (!IsValidBounds(new RectInt(x, y, 1, 1))) continue;
                    
                    // 이미 복도(2)이거나 문(3)인 곳을 찾음
                    if (grid[x, y] == 2 || grid[x, y] == 3)
                    {
                        float d = Vector2.Distance(start, new Vector2(x, y));
                        // 너무 가까운(이미 붙어있는) 곳 제외, 너무 먼 곳 제외
                        if (d > 1.5f && d < minDst)
                        {
                            minDst = d;
                            bestTarget = new Vector2Int(x, y);
                            found = true;
                        }
                    }
                }
            }

            if (found)
            {
                // [수정] 바닥을 깔 폴더 찾기 (Corridors/Floors)
                Transform floorParent = parent; // 기본값 (못 찾으면 Corridors에)
                Transform floors = parent.Find("Floors");
                if (floors != null) floorParent = floors;

                int currX = start.x, currY = start.y;
                List<Vector2Int> path = new List<Vector2Int>();
                
                while (currX != bestTarget.x) { path.Add(new Vector2Int(currX, currY)); currX += (bestTarget.x > currX) ? 1 : -1; }
                while (currY != bestTarget.y) { path.Add(new Vector2Int(currX, currY)); currY += (bestTarget.y > currY) ? 1 : -1; }

                foreach (var p in path)
                {
                    if (grid[p.x, p.y] == 1 || grid[p.x, p.y] == 3) continue;
                    if (grid[p.x, p.y] != 2)
                    {
                        grid[p.x, p.y] = 2; 
                        if (floorPrefab)
                        {
                            float u = profile.unitSize;
                            Vector3 pos = new Vector3(p.x * u, 0, p.y * u) + profile.corridorFloorOffset;
                            // [수정] floorParent 사용
                            Instantiate(floorPrefab, pos, Quaternion.identity, floorParent);
                        }
                    }
                }
            }
        }

        // -------------------------------------------------------
        // Step 6: Spawning
        // -------------------------------------------------------
        void SpawnRoomPrefabs(Transform parent)
        {
            foreach (Transform c in parent) if(c.name.Contains("Room_")) Destroy(c.gameObject);
            float u = profile.unitSize;

            foreach (var r in placedRooms)
            {
                if (r.data == null || r.data.prefab == null) continue;
                float xPos = r.bounds.x * u; float zPos = r.bounds.y * u;
                float w = r.data.width * u; float h = r.data.height * u;

                switch (r.rotationAngle) {
                    case 90: zPos += w; break;
                    case 180: xPos += w; zPos += h; break;
                    case 270: xPos += h; break;
                }
                GameObject go = Instantiate(r.data.prefab, new Vector3(xPos, currentFloorHeight, zPos), Quaternion.Euler(0, r.rotationAngle, 0), parent);
                r.spawnedObject = go;
                MapTile tile = go.GetComponent<MapTile>();
                if (tile) {
                    for (int rx = 0; rx < r.bounds.width; rx++)
                        for (int ry = 0; ry < r.bounds.height; ry++)
                        {
                            Vector2Int gridPos = new Vector2Int(r.bounds.x + rx, r.bounds.y + ry);
                            if (!tileMap.ContainsKey(gridPos)) tileMap.Add(gridPos, tile);
                        }
                }
            }
        }

        void SpawnCorridors(Transform parent)
        {
            var root = new GameObject("Corridors") { transform = { parent = parent, localPosition = Vector3.zero } };
            float u = profile.unitSize;

            for (int x = 0; x < profile.mapWidth; x++)
                for (int y = 0; y < profile.mapHeight; y++)
                    if (grid[x, y] == 2 || grid[x, y] == 3) 
                    {
                        
                        var p = GetRandomFloorPrefab();
                        if (p) {
                            var go = Instantiate(p, new Vector3(x * u, currentFloorHeight, y * u) + profile.corridorFloorOffset, Quaternion.identity, root.transform);
                            go.isStatic = true; // [최적화] 움직이지 않음!
                            var tile = go.GetComponent<MapTile>(); if(tile) tileMap[new Vector2Int(x, y)] = tile;
                        }
                    }

            ScanGridAndBuildWalls(true, true, root.transform);
            ScanGridAndBuildWalls(true, false, root.transform);
            ScanGridAndBuildWalls(false, true, root.transform);
            ScanGridAndBuildWalls(false, false, root.transform);
        }

        void SpawnDoors(Transform parent)
        {
            HashSet<Vector2Int> processedSockets = new HashSet<Vector2Int>();
            HashSet<WallBoundary> occupiedBoundaries = new HashSet<WallBoundary>();
            List<Vector3> spawnedPositions = new List<Vector3>(); 

            float u = profile.unitSize;
            GameObject floorPrefab = GetRandomFloorPrefab(); 
            Transform corridorRoot = parent.Find("Corridors") ?? parent;

            Dictionary<RoomInstance, List<DoorCluster>> roomToClusters = new Dictionary<RoomInstance, List<DoorCluster>>();
            foreach (var c in plannedClusters)
            {
                if (!roomToClusters.ContainsKey(c.owner)) roomToClusters[c.owner] = new List<DoorCluster>();
                roomToClusters[c.owner].Add(c);
            }

            // ========================================================================
            // [수정됨] 1. 더블 도어 우선 시도 -> 2. 실패 시 싱글 도어 생성 (빈틈 없이 채우기)
            // ========================================================================
            System.Func<DoorCluster, int, int> TrySpawnInCluster = (cluster, limit) => 
            {
                List<Vector2Int> sortedSockets;
                if (cluster.dir.y != 0) sortedSockets = cluster.sockets.OrderBy(s => s.x).ToList();
                else sortedSockets = cluster.sockets.OrderBy(s => s.y).ToList();

                int spawnedCount = 0;
                int i = 0;

                while (i < sortedSockets.Count)
                {
                    if (spawnedCount >= limit) break; 

                    Vector2Int currentPos = sortedSockets[i];
                    if (processedSockets.Contains(currentPos)) { i++; continue; }

                    // --- [1단계] 더블 도어 가능성 체크 ---
                    bool isDouble = false; 
                    Vector2Int nextPos = Vector2Int.zero;
                    
                    // 다음 소켓이 있고, 물리적으로 붙어있으며(거리1), 아직 처리 안 됐는지 확인
                    if (i + 1 < sortedSockets.Count) {
                        nextPos = sortedSockets[i + 1];
                        
                        if (Vector2Int.Distance(currentPos, nextPos) == 1 && !processedSockets.Contains(nextPos)) 
                        {
                            // 확률 체크 (1.0이면 무조건 통과)
                            if (CoreRandom.Value() < profile.doubleDoorChance)
                            {
                                isDouble = true;
                            }
                        }
                    }

                    // --- [공간 점유 체크] ---
                    WallBoundary b1 = new WallBoundary(currentPos, currentPos + cluster.dir);
                    WallBoundary b2 = default;
                    bool isOccupied = occupiedBoundaries.Contains(b1);
                    
                    if (isDouble) { 
                        b2 = new WallBoundary(nextPos, nextPos + cluster.dir); 
                        // 더블 도어인데 둘 중 한 칸이라도 막혀있으면, 더블 도어 포기 -> 싱글로 전환 시도?
                        // 여기서는 "더블 도어 자리가 막혀있으면 설치 불가"로 판단하되,
                        // 싱글로라도 뚫을지 결정해야 함. 보통은 Occupied면 문이 이미 있는 거니까 패스.
                        if (occupiedBoundaries.Contains(b2)) isOccupied = true; 
                    }

                    // --- [물리적 구멍 뚫기 (필수)] ---
                    // Occupied(문이 있음) 여부와 상관없이 내 벽은 무조건 뚫어야 함
                    ProcessSocketWallAndFloor(currentPos, cluster.dir, floorPrefab, corridorRoot, u);
                    processedSockets.Add(currentPos);

                    if (isDouble) {
                        ProcessSocketWallAndFloor(nextPos, cluster.dir, floorPrefab, corridorRoot, u);
                        processedSockets.Add(nextPos);
                    }

                    // --- [문 오브젝트 생성 (선택)] ---
                    if (!isOccupied) {
                        Vector3 spawnCenter; 
                        GameObject doorPrefab;

                        if (isDouble) {
                            spawnCenter = (new Vector3(currentPos.x,0,currentPos.y)*u + new Vector3(nextPos.x,0,nextPos.y)*u + Vector3.one*u)*0.5f; 
                            spawnCenter.y = currentFloorHeight;
                            doorPrefab = PickRandomDoor(profile.doubleDoors, false);
                        } else {
                            // 싱글 도어
                            spawnCenter = new Vector3(currentPos.x*u + u*0.5f, currentFloorHeight, currentPos.y*u + u*0.5f);
                            doorPrefab = PickRandomDoor(profile.swingDoors, false);
                        }

                        if (doorPrefab) {
                            Vector3 finalPos = spawnCenter + new Vector3(cluster.dir.x, 0, cluster.dir.y) * (u * 0.5f);
                            
                            bool posOverlap = false;
                            foreach(var p in spawnedPositions) { if(Vector3.Distance(p, finalPos) < 0.1f) { posOverlap=true; break; }}

                            if (!posOverlap) {
                                Quaternion rot = Quaternion.LookRotation(new Vector3(cluster.dir.x, 0, cluster.dir.y));
                                Transform targetParent = (cluster.owner != null && cluster.owner.spawnedObject != null) ? cluster.owner.spawnedObject.transform : corridorRoot;
                                Instantiate(doorPrefab, finalPos, rot, targetParent);
                                
                                // 경계선 등록
                                occupiedBoundaries.Add(b1); 
                                createdDoorBoundaries.Add(b1); // [중요] ConnectAdjacentRooms를 위해 등록
                                
                                if (isDouble) {
                                    occupiedBoundaries.Add(b2);
                                    createdDoorBoundaries.Add(b2);
                                }

                                spawnedPositions.Add(finalPos);
                                spawnedCount++;
                            }
                        }
                    }
                    
                    // [인덱스 이동] 더블이면 2칸, 싱글이면 1칸
                    i += isDouble ? 2 : 1;
                }
                return spawnedCount;
            };

            // 2. 실행 루프 (기존 유지)
            foreach (var kvp in roomToClusters)
            {
                List<DoorCluster> clusters = kvp.Value;
                int targetCount = 1;
                if (profile.extraDoorCountChances != null) {
                    for (int i = 0; i < profile.extraDoorCountChances.Count; i++) {
                        if (CoreRandom.Value() < profile.extraDoorCountChances[i]) targetCount = 2 + i; else break;
                    }
                }

                int currentCount = 0;
                foreach (var cluster in clusters)
                {
                    if (currentCount >= targetCount) break;
                    if (TrySpawnInCluster(cluster, 1) > 0) currentCount++;
                }

                if (currentCount < targetCount)
                {
                    var shuffled = clusters.OrderBy(x => CoreRandom.Value()).ToList();
                    foreach (var cluster in shuffled)
                    {
                        if (currentCount >= targetCount) break;
                        int added = TrySpawnInCluster(cluster, targetCount - currentCount);
                        currentCount += added;
                    }
                }
            }
        }
        
        void ConnectAdjacentRooms()
        {
            float u = profile.unitSize;
            Transform corridorRoot = mapRoot.transform.Find("Corridors") ?? mapRoot.transform;
            GameObject floorPrefab = GetRandomFloorPrefab();

            foreach (var room in placedRooms)
            {
                if (room.data == null) continue;

                // 이 방의 모든 소켓을 뒤짐
                int count = Mathf.Min(room.data.possibleDoorSpots.Count, room.data.doorDirections.Count);
                
                // 벽면(방향)별로 이미 문이 있는지 체크하기 위해 그룹화
                // Key: Direction, Value: List of sockets
                var socketsByDir = new Dictionary<Vector2Int, List<DoorSocket>>();

                for (int i = 0; i < count; i++) {
                    Vector2Int gPos = TransformPoint(room.data.possibleDoorSpots[i], room);
                    Vector2Int dir = RotateVector(room.data.doorDirections[i], room.rotationAngle);
                    
                    if (!socketsByDir.ContainsKey(dir)) socketsByDir[dir] = new List<DoorSocket>();
                    socketsByDir[dir].Add(new DoorSocket { gridPos = gPos, forward = dir, entryPos = gPos + dir });
                }

                // 각 벽면(방향)을 검사
                foreach (var kvp in socketsByDir)
                {
                    Vector2Int dir = kvp.Key;
                    List<DoorSocket> sockets = kvp.Value;

                    // 1. 이 벽면에 "이미 문이 있는가?" (복도 문 포함)
                    bool hasDoorOnThisWall = false;
                    foreach (var s in sockets) {
                        if (createdDoorBoundaries.Contains(new WallBoundary(s.gridPos, s.entryPos))) {
                            hasDoorOnThisWall = true;
                            break;
                        }
                    }
                    if (hasDoorOnThisWall) continue; // 이미 있으면 패스

                    // 2. 이 벽면 너머에 "방이 있는가?" (맞닿은 방)
                    // 소켓 하나라도 맞은편이 방이라면 '맞닿은 벽'으로 간주
                    DoorSocket validLinkSocket = null;
                    foreach (var s in sockets) {
                        Vector2Int target = s.entryPos;
                        if (IsValidBounds(new RectInt(target.x, target.y, 1, 1)) && grid[target.x, target.y] == 1) {
                            // 맞은편이 방이고, 뚫을 수 있는 소켓 위치라면
                            validLinkSocket = s;
                            break; // 하나만 찾으면 됨 (벽 하나당 문 1개)
                        }
                    }

                    // 3. 연결 실행
                    if (validLinkSocket != null)
                    {
                        // 문 생성 (오브젝트)
                        Vector3 spawnCenter = new Vector3(validLinkSocket.gridPos.x * u + u * 0.5f, currentFloorHeight, validLinkSocket.gridPos.y * u + u * 0.5f);
                        Vector3 finalPos = spawnCenter + new Vector3(dir.x, 0, dir.y) * (u * 0.5f);
                        Quaternion rot = Quaternion.LookRotation(new Vector3(dir.x, 0, dir.y));
                        
                        var prefab = PickRandomDoor(profile.swingDoors, false); // 방 연결은 스윙 도어 기본
                        if (prefab) {
                            Transform targetParent = (room.spawnedObject != null) ? room.spawnedObject.transform : corridorRoot;
                            Instantiate(prefab, finalPos, rot, targetParent);
                        }

                        // 벽 뚫기 (양쪽 다)
                        ProcessSocketWallAndFloor(validLinkSocket.gridPos, dir, floorPrefab, corridorRoot, u);
                        
                        // 기록 (상대방 방도 이 벽면을 처리할 때 "문 있음"으로 인식하게)
                        createdDoorBoundaries.Add(new WallBoundary(validLinkSocket.gridPos, validLinkSocket.entryPos));
                    }
                }
            }
        }

        void GenerateBranchingCorridors()
        {
            // 1. 확률이 0이면 아예 실행 안 함
            if (profile.extraConnectionChance <= 0) return;

            int attempts = profile.branchingAttempts;
            int created = 0;

            for (int k = 0; k < attempts; k++)
            {
                // 1. 랜덤한 복도 지점 선택
                int x = CoreRandom.Range(1, profile.mapWidth - 1);
                int y = CoreRandom.Range(1, profile.mapHeight - 1);
                if (grid[x, y] != 2) continue; 

                // 2. 뚫을 수 있는 방향(빈 공간 0) 찾기
                List<Vector2Int> validDirs = new List<Vector2Int>();
                Vector2Int[] dirs = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
                
                foreach (var d in dirs)
                {
                    if (IsValidBounds(new RectInt(x + d.x, y + d.y, 1, 1)) && grid[x + d.x, y + d.y] == 0)
                        validDirs.Add(d);
                }
                
                if (validDirs.Count == 0) continue;

                // 3. 굴착 시작
                Vector2Int dir = validDirs[CoreRandom.Range(0, validDirs.Count)];
                Vector2Int current = new Vector2Int(x, y);
                List<Vector2Int> path = new List<Vector2Int>();
                bool success = false;

                // 최대 30칸(혹은 10칸) 정도 뚫어봄
                for (int step = 0; step < 50; step++)
                {
                    current += dir;
                    if (!IsValidBounds(new RectInt(current.x, current.y, 1, 1))) break;

                    int cell = grid[current.x, current.y];
                    
                    // 성공: 다른 복도(2)나 문(3)을 만남
                    if (cell == 2 || cell == 3)
                    {
                        // 너무 짧은(1~2칸) 샛길은 의미 없으니 제외 (선택사항)
                        if (path.Count >= 2) success = true;
                        break;
                    }
                    // 실패: 방(1)을 만남
                    if (cell == 1) break;

                    path.Add(current);

                    // (선택) 가끔 방향 틀기
                    if (CoreRandom.Value() < 0.2f)
                    {
                         var turnOpts = new[] { new Vector2Int(-dir.y, dir.x), new Vector2Int(dir.y, -dir.x) };
                         // 맵 밖이나 방으로 꺾지 않도록 체크
                         var validTurns = turnOpts.Where(t => IsValidBounds(new RectInt(current.x+t.x, current.y+t.y, 1,1)) && grid[current.x + t.x, current.y + t.y] == 0).ToList();
                         if (validTurns.Count > 0) dir = validTurns[CoreRandom.Range(0, validTurns.Count)];
                    }
                }

                // 4. 성공 시 적용 (여기서 확률 변수 사용!)
                if (success && path.Count > 0)
                {
                    // [핵심 연결] 성공했더라도 확률(Chance)에 따라 최종 결정
                    if (CoreRandom.Value() < profile.extraConnectionChance)
                    {
                        foreach (var p in path) grid[p.x, p.y] = 2;
                        created++;
                    }
                }
            }
            Debug.Log($"[Branch] Created {created} extra shortcuts.");
        }

        
        void SpawnCorridorDoors(Transform parent)
        {
            // 1. 생성 확률(Chance) 체크
            if (profile.corridorDoorChance <= 0) return;
            float u = profile.unitSize;
            
            Transform root = parent.Find("Corridors/Doors");
            if (root == null) {
                Transform cr = parent.Find("Corridors") ?? parent;
                root = new GameObject("Doors").transform;
                root.SetParent(cr);
            }

            HashSet<Vector2Int> processed = new HashSet<Vector2Int>();
            
            Dictionary<int, List<int>> horizontalDoorLines = new Dictionary<int, List<int>>();
            Dictionary<int, List<int>> verticalDoorLines = new Dictionary<int, List<int>>();

            // Helper Functions
            bool IsTooClose(Dictionary<int, List<int>> dict, int lineIndex, int posIndex)
            {
                if (!dict.ContainsKey(lineIndex)) return false;
                foreach (var existingPos in dict[lineIndex])
                    if (Mathf.Abs(existingPos - posIndex) < profile.corridorDoorDistance) return true;
                return false;
            }

            void RegisterDoor(Dictionary<int, List<int>> dict, int lineIndex, int posIndex)
            {
                if (!dict.ContainsKey(lineIndex)) dict[lineIndex] = new List<int>();
                dict[lineIndex].Add(posIndex);
            }

            bool IsPath(int cx, int cy) => IsValidBounds(new RectInt(cx, cy, 1, 1)) && (grid[cx, cy] == 2);
            bool IsWall(int cx, int cy) => !IsValidBounds(new RectInt(cx, cy, 1, 1)) || (grid[cx, cy] != 2 && grid[cx, cy] != 3);

            // 순회 시작
            for (int x = 1; x < profile.mapWidth - 1; x++)
            {
                for (int y = 1; y < profile.mapHeight - 1; y++)
                {
                    Vector2Int pos = new Vector2Int(x, y);
                    if (processed.Contains(pos)) continue;
                    if (grid[x, y] != 2) continue;
                    if (CoreRandom.Value() > profile.corridorDoorChance) continue;
                    if (protectedRingTiles.Contains(pos)) continue;

                    // -------------------------------------------------------
                    // Case A: 가로(Horizontal) 흐름
                    // -------------------------------------------------------
                    if (IsPath(x-1, y) && IsPath(x+1, y))
                    {
                        // [검사] 위쪽 벽(x, y+1)이나 아래쪽 벽(x, y-1)이 기능성 벽인가?
                        // 기능성 벽 옆에는 문을 설치하지 않음 (자판기 가림 방지)
                        if (functionalWallPositions.Contains(new Vector2Int(x, y + 1)) || 
                            functionalWallPositions.Contains(new Vector2Int(x, y - 1))) 
                            continue;

                        // [우선순위 1] 2칸 문 (더블 도어)
                        if (IsPath(x, y+1) && IsPath(x-1, y+1) && IsPath(x+1, y+1) && 
                            IsWall(x, y+2) && IsWall(x, y-1))
                        {
                            // 2칸 문일 때는 확장된 영역의 벽도 검사
                            if (functionalWallPositions.Contains(new Vector2Int(x, y + 2))) continue;

                            if (!IsTooClose(horizontalDoorLines, y, x) && !IsTooClose(horizontalDoorLines, y+1, x))
                            {
                                processed.Add(pos); processed.Add(new Vector2Int(x, y+1));
                                grid[x, y] = 3; grid[x, y+1] = 3;
                                
                                Vector3 spawnPos = new Vector3(x * u + u*0.5f, currentFloorHeight, (y + 1) * u); 
                                var prefab = PickRandomDoor(profile.doubleDoors, true);
                                if(prefab) Instantiate(prefab, spawnPos, Quaternion.Euler(0, 90, 0), root);

                                RegisterDoor(horizontalDoorLines, y, x);
                                RegisterDoor(horizontalDoorLines, y+1, x);
                                continue; 
                            }
                        }
                        
                        // [우선순위 2] 1칸 문 (싱글 도어)
                        if (IsWall(x, y+1) && IsWall(x, y-1))
                        {
                            if (!IsTooClose(horizontalDoorLines, y, x))
                            {
                                processed.Add(pos); grid[x, y] = 3;
                                Vector3 spawnPos = new Vector3(x * u + u*0.5f, currentFloorHeight, y * u + u*0.5f);
                                var prefab = PickRandomDoor(profile.swingDoors, true);
                                if(prefab) Instantiate(prefab, spawnPos, Quaternion.Euler(0, 90, 0), root);

                                RegisterDoor(horizontalDoorLines, y, x);
                                continue;
                            }
                        }
                    }
                    
                    // -------------------------------------------------------
                    // Case B: 세로(Vertical) 흐름
                    // -------------------------------------------------------
                    if (IsPath(x, y-1) && IsPath(x, y+1))
                    {
                        // [검사] 오른쪽 벽(x+1, y)이나 왼쪽 벽(x-1, y)이 기능성 벽인가?
                        if (functionalWallPositions.Contains(new Vector2Int(x + 1, y)) || 
                            functionalWallPositions.Contains(new Vector2Int(x - 1, y))) 
                            continue;

                        // [우선순위 1] 2칸 문 (더블 도어)
                        if (IsPath(x+1, y) && IsPath(x+1, y-1) && IsPath(x+1, y+1) &&
                            IsWall(x+2, y) && IsWall(x-1, y))
                        {
                            // 2칸 문 확장 영역 검사
                            if (functionalWallPositions.Contains(new Vector2Int(x + 2, y))) continue;

                            if (!IsTooClose(verticalDoorLines, x, y) && !IsTooClose(verticalDoorLines, x+1, y)) 
                            {
                                processed.Add(pos); processed.Add(new Vector2Int(x+1, y));
                                grid[x, y] = 3; grid[x+1, y] = 3;
                                
                                Vector3 spawnPos = new Vector3((x + 1) * u, currentFloorHeight, y * u + u*0.5f);
                                var prefab = PickRandomDoor(profile.doubleDoors, true);
                                if(prefab) Instantiate(prefab, spawnPos, Quaternion.Euler(0, 0, 0), root);

                                RegisterDoor(verticalDoorLines, x, y);
                                RegisterDoor(verticalDoorLines, x+1, y);
                                continue;
                            }
                        }
                        
                        // [우선순위 2] 1칸 문 (싱글 도어)
                        if (IsWall(x+1, y) && IsWall(x-1, y))
                        {
                            if (!IsTooClose(verticalDoorLines, x, y))
                            {
                                processed.Add(pos); grid[x, y] = 3;
                                Vector3 spawnPos = new Vector3(x * u + u*0.5f, currentFloorHeight, y * u + u*0.5f);
                                var prefab = PickRandomDoor(profile.swingDoors, true);
                                if(prefab) Instantiate(prefab, spawnPos, Quaternion.Euler(0, 0, 0), root);

                                RegisterDoor(verticalDoorLines, x, y);
                                continue;
                            }
                        }
                    }
                }
            }
        }

        void SpawnCeilingLights(Transform parent)
        {
            if (profile.ceilingObjects == null || profile.ceilingObjects.Count == 0) return;

            // 1. [그룹화] 복도 타일들을 스캔하여 '조명 후보 위치' 리스트 생성
            List<Vector3> candidates = new List<Vector3>();
            bool[,] visited = new bool[profile.mapWidth, profile.mapHeight];
            float u = profile.unitSize;

            for (int x = 0; x < profile.mapWidth; x++)
            {
                for (int y = 0; y < profile.mapHeight; y++)
                {
                    if (visited[x, y]) continue;
                    
                    // 문(3)이나 빈 땅(0)은 제외하고, 오직 '순수 복도(2)'만 대상
                    if (grid[x, y] != 2) continue;

                    visited[x, y] = true;
                    Vector3 spawnPos;

                    // A. 가로 2칸 체크 (오른쪽도 복도인가?)
                    // 조건: 맵 안쪽 + 복도(2) + 방문 안함
                    if (x + 1 < profile.mapWidth && grid[x + 1, y] == 2 && !visited[x + 1, y])
                    {
                        visited[x + 1, y] = true;
                        // 위치: 두 타일의 경계선 (x+1 지점)
                        spawnPos = new Vector3((x + 1) * u, profile.lightHeight + currentFloorHeight, y * u + u * 0.5f);
                        candidates.Add(spawnPos);
                        continue;
                    }

                    // B. 세로 2칸 체크 (위쪽도 복도인가?)
                    if (y + 1 < profile.mapHeight && grid[x, y + 1] == 2 && !visited[x, y + 1])
                    {
                        visited[x, y + 1] = true;
                        // 위치: 두 타일의 경계선 (y+1 지점)
                        spawnPos = new Vector3(x * u + u * 0.5f, profile.lightHeight + currentFloorHeight, (y + 1) * u);
                        candidates.Add(spawnPos);
                        continue;
                    }

                    // C. 1칸 (싱글)
                    // 위치: 타일의 정중앙
                    spawnPos = new Vector3(x * u + u * 0.5f, profile.lightHeight + currentFloorHeight, y * u + u * 0.5f);
                    candidates.Add(spawnPos);
                }
            }

            // 2. [선발] 셔플 & 거리 제한 적용 & 생성
            candidates = candidates.OrderBy(x => CoreRandom.Value()).ToList();
            List<Vector3> placedPositions = new List<Vector3>();
            Transform lightRoot = parent.Find("Lights");
            if (lightRoot == null) {
                lightRoot = new GameObject("Lights").transform;
                lightRoot.SetParent(parent);
            }

            // 거리 체크용 제곱값 (성능 최적화)
            float minSqDist = (profile.minLightSpacing * u) * (profile.minLightSpacing * u);

            foreach (var pos in candidates)
            {
                // 확률 체크
                if (CoreRandom.Value() > profile.lightSpawnChance) continue;

                // 거리 체크
                bool tooClose = false;
                foreach (var placed in placedPositions)
                {
                    if ((pos - placed).sqrMagnitude < minSqDist)
                    {
                        tooClose = true;
                        break;
                    }
                }
                if (tooClose) continue;

                // 생성
                GameObject prefab = PickRandomCeilingObject(profile.ceilingObjects);
                if (prefab)
                {
                    // 천장에 붙어야 하므로 X축 180도 회전 (프리팹 축에 따라 다를 수 있음)
                    var go = Instantiate(prefab, pos, Quaternion.Euler(180, 0, 0), lightRoot);
                    go.isStatic = true;
                    placedPositions.Add(pos);
                }
            }
        }

        // -------------------------------------------------------
        // Helpers
        // -------------------------------------------------------
        void ScanGridAndBuildWalls(bool isHorizontal, bool lookPositive, Transform parent)
        {
            int outerMax = isHorizontal ? profile.mapHeight : profile.mapWidth;
            int innerMax = isHorizontal ? profile.mapWidth : profile.mapHeight;
            for (int line = 0; line < outerMax; line++) {
                int start = -1, len = 0;
                for (int i = 0; i < innerMax; i++) {
                    int x = isHorizontal ? i : line; int y = isHorizontal ? line : i;
                    if (CheckWallNeeded(x, y, isHorizontal, lookPositive)) { if (start == -1) start = i; len++; }
                    else if (start != -1) { FillSegment(start, line, len, isHorizontal, lookPositive, parent); start = -1; len = 0; }
                }
                if (start != -1) FillSegment(start, line, len, isHorizontal, lookPositive, parent);
            }
        }
        bool CheckWallNeeded(int x, int y, bool isH, bool isPos) {
            if (grid[x, y] != 2) return false; 
            int tx = x + (isH ? 0 : (isPos ? 1 : -1)); int ty = y + (isH ? (isPos ? 1 : -1) : 0);
            if (tx < 0 || tx >= profile.mapWidth || ty < 0 || ty >= profile.mapHeight) return true;
            // 문(3)이나 방(1) 앞에는 벽 안 세움
            if (grid[tx, ty] == 1 || grid[tx, ty] == 3) return false;
            return grid[tx, ty] == 0;
        }
        void FillSegment(int start, int line, int len, bool isH, bool isPos, Transform p) 
        {
            int u = (int)profile.unitSize; 
            int cur = start;
            
            // 벽이 바라보는(튀어나올) 방향 벡터 계산
            // 가로 복도(isH): 위쪽(isPos)이면 +y, 아래쪽이면 -y
            // 세로 복도(!isH): 오른쪽(isPos)이면 +x, 왼쪽이면 -x
            Vector2Int depthDir = Vector2Int.zero;
            if (isH) depthDir = isPos ? Vector2Int.up : Vector2Int.down;
            else depthDir = isPos ? Vector2Int.right : Vector2Int.left;

            while (len > 0) 
            {
                // 1. 후보군 추리기 (남은 길이(len)보다 작거나 같은 벽들)
                var cands = new List<WallPrefabData>();
                if(profile.corridorWallBasic != null && profile.corridorWallBasic.prefab != null) 
                    cands.Add(new WallPrefabData { prefab = profile.corridorWallBasic.prefab, size = 1, chance = 10f });
                
                if(profile.wallPrefabs != null) 
                    cands.AddRange(profile.wallPrefabs.Where(w => w.size <= len));
                
                if (cands.Count == 0) break;

                // 2. 랜덤 뽑기
                float r = CoreRandom.Value() * cands.Sum(w => w.chance), s = 0; 
                var pick = cands.Last();
                foreach (var c in cands) { s += c.chance; if (r <= s) { pick = c; break; } }

                // ---------------------------------------------------------
                // [Pre-check] 기능성 벽이라면 공간 검사 수행
                // ---------------------------------------------------------
                bool isValid = true;
                if (pick.isFunctional)
                {
                    // 벽이 차지하는 모든 칸(size)에 대해 검사
                    for (int k = 0; k < pick.size; k++)
                    {
                        // 현재 벽의 기준 좌표 (복도 바로 옆)
                        int checkX = isH ? (cur + k) : line + (isPos ? 1 : -1);
                        int checkY = isH ? line + (isPos ? 1 : -1) : (cur + k);

                        // Depth만큼 뒤쪽을 찔러봄 (1칸 뒤부터 depth칸 뒤까지)
                        for (int d = 1; d <= pick.depth; d++)
                        {
                            int deepX = checkX + (depthDir.x * d);
                            int deepY = checkY + (depthDir.y * d);

                            // 맵 밖이거나, 빈 공허(0)가 아니면 실패 (방, 복도, 문 등 침범 불가)
                            if (!IsValidBounds(new RectInt(deepX, deepY, 1, 1)) || grid[deepX, deepY] != 0)
                            {
                                isValid = false;
                                break;
                            }
                        }
                        if (!isValid) break;
                    }
                }

                // 검사 실패 시 기본 벽으로 강제 교체
                if (!isValid)
                {
                    pick = new WallPrefabData { 
                        prefab = profile.corridorWallBasic.prefab, 
                        size = 1, // 기본 벽 사이즈 1 가정
                        isFunctional = false 
                    };
                }

                // ---------------------------------------------------------
                // [설치] 좌표 계산 및 Instantiate
                // ---------------------------------------------------------
                float xPos = (isH ? cur : line) * u; 
                float zPos = (isH ? line : cur) * u;
                
                Vector3 finalPos = new Vector3(xPos, currentFloorHeight, zPos); 
                float angle = isH ? (isPos ? 0 : 180) : (isPos ? 90 : 270);
                
                if(isH) { if(isPos) finalPos.z += u; else finalPos.x += pick.size * u; } 
                else { if(isPos) { finalPos.x += u; finalPos.z += pick.size * u; } }
                
                var go = Instantiate(pick.prefab, finalPos, Quaternion.Euler(0, angle, 0), p);
                go.isStatic = true;

                // ---------------------------------------------------------
                // [등록] 기능성 벽이라면 좌표 등록 (문 생성 방지용)
                // ---------------------------------------------------------
                if (pick.isFunctional)
                {
                    for (int k = 0; k < pick.size; k++)
                    {
                        int wallX = isH ? (cur + k) : line + (isPos ? 1 : -1);
                        int wallY = isH ? line + (isPos ? 1 : -1) : (cur + k);
                        functionalWallPositions.Add(new Vector2Int(wallX, wallY));
                    }
                }

                len -= pick.size; 
                cur += pick.size;
            }
        }
        void SortAndRenameRooms() 
        {
            // 1. 방 정렬 및 이름 변경 (기존 로직)
            Vector2 referencePoint = new Vector2(0, profile.mapHeight);
            var startRoom = placedRooms.Find(r => r.currentType == RoomType.Start);
            var otherRooms = placedRooms.Where(r => r.currentType != RoomType.Start).ToList();
            otherRooms = otherRooms.OrderBy(r => Vector2.Distance(r.Center, referencePoint)).ToList();
            
            if (startRoom != null && startRoom.spawnedObject != null) { 
                startRoom.spawnedObject.name = "(1)Start_Room"; 
                startRoom.spawnedObject.transform.SetAsFirstSibling(); 
            }
            
            int esc=0;
            for(int i=0; i<otherRooms.Count; i++) {
                var r = otherRooms[i]; if(!r.spawnedObject) continue;
                string id = ""; 
                if(r.data.prefab) { 
                    string[] parts = r.data.prefab.name.Replace("(Clone)","").Split('_'); 
                    id = parts.Length > 0 ? parts.Last() : "0"; 
                }
                string pre = r.currentType == RoomType.Escape ? $"E{++esc}" : r.currentType.ToString().Substring(0,1);
                r.spawnedObject.name = $"({i+2}){pre}_{r.data.width}{r.data.height}_{id}"; // i+2을 하는 건 0번 방 원천 제외와 시작 방 1개를 고려한 것
                r.spawnedObject.transform.SetSiblingIndex(i+1);
            }

            // =========================================================
            // [추가] 하이어라키 강제 정리 (Cleanup)
            // =========================================================
            
            // 1. 폴더 확보
            Transform corridors = mapRoot.transform.Find("Corridors");
            if (corridors == null) {
                corridors = new GameObject("Corridors").transform;
                corridors.SetParent(mapRoot.transform);
            }

            Transform floorsFolder = corridors.Find("Floors");
            if (floorsFolder == null) {
                floorsFolder = new GameObject("Floors").transform;
                floorsFolder.SetParent(corridors);
            }

            // 2. mapRoot 바로 아래에 있는 "Floor_"로 시작하는 오브젝트 식별
            // (주의: foreach 도중에 부모를 바꾸면 트리가 변경되므로 리스트에 담았다가 옮겨야 함)
            List<Transform> looseFloors = new List<Transform>();
            foreach (Transform child in mapRoot.transform)
            {
                // 이름에 Floor가 들어있고, Corridors나 Lights 같은 폴더가 아닌 것
                if (child.name.Contains("Floor") && child != corridors)
                {
                    looseFloors.Add(child);
                }
            }

            // 3. 강제 이주
            foreach (var floor in looseFloors)
            {
                floor.SetParent(floorsFolder);
            }

            // =========================================================

            // 최종 순서 정렬
            foreach (var e in otherRooms.Where(r => r.currentType == RoomType.Escape)) 
                if(e.spawnedObject) e.spawnedObject.transform.SetAsLastSibling();
            
            if(corridors) corridors.SetAsLastSibling();
            
            Transform lr = mapRoot.transform.Find("Lights"); 
            if(lr) lr.SetAsLastSibling();
        }

        void EnsureCorridorFloorExists(Vector2Int pos, GameObject prefab, Transform parent)
        {
            // 맵 밖이면 패스
            if (!IsValidBounds(new RectInt(pos.x, pos.y, 1, 1))) return;

            // 이미 방(1)이거나 복도(2)면 패스. 
            // (단, 문 자리(3)는 바닥을 깔아야 하므로 0 또는 3일 때 진행)
            // 여기서는 "0(빈공간)이거나 3(문)일 때" 바닥을 깝니다.
            if (grid[pos.x, pos.y] == 1 || grid[pos.x, pos.y] == 2) return;

            // 데이터 갱신 (복도로 취급)
            grid[pos.x, pos.y] = 2;

            // 물리적 생성
            if (prefab != null)
            {
                float u = profile.unitSize;
                Vector3 worldPos = new Vector3(pos.x * u, currentFloorHeight, pos.y * u) + profile.corridorFloorOffset;
                
                var go = Instantiate(prefab, worldPos, Quaternion.identity, parent);
                go.isStatic = true;
                
                // 타일맵에 등록 (나중에 벽 뚫기 등을 위해)
                MapTile tile = go.GetComponent<MapTile>();
                if (tile && !tileMap.ContainsKey(pos)) 
                {
                    tileMap.Add(pos, tile);
                }
            }
        }

        // 해당 타일(tile)의 자식 소켓 중 targetPos와 가장 가까운 것을 찾아 비활성화(벽 뚫기)
        RoomSocket FindAndDisableSocket(MapTile tile, Vector3 targetPos)
        {
            // 탐색 범위: 유닛 사이즈의 90% (거의 타일 전체 커버)
            float minDst = profile.unitSize * 0.9f; 
            RoomSocket closest = null;

            // 타일 안에 있는 모든 소켓 검색
            foreach (var s in tile.GetComponentsInChildren<RoomSocket>())
            {
                if (!s.gameObject.activeSelf) continue;

                // 높이 무시하고 수평 거리만 비교
                Vector3 sPos = s.transform.position;
                sPos.y = targetPos.y; 

                float d = Vector3.Distance(sPos, targetPos);
                if (d < minDst)
                {
                    minDst = d;
                    closest = s;
                }
            }

            // 찾았으면 끄기 (벽 오브젝트가 꺼짐)
            if (closest != null)
            {
                closest.gameObject.SetActive(false);
            }
            
            return closest;
        }
        
        // Helpers
        Vector2Int TransformPoint(Vector2Int l, RoomInstance r) { int lx=l.x, ly=l.y, w=r.data.width, h=r.data.height; switch(r.rotationAngle){ case 90:return new Vector2Int(r.bounds.x+ly, r.bounds.y+(w-1-lx)); case 180:return new Vector2Int(r.bounds.x+(w-1-lx), r.bounds.y+(h-1-ly)); case 270:return new Vector2Int(r.bounds.x+(h-1-ly), r.bounds.y+lx); default:return new Vector2Int(r.bounds.x+lx, r.bounds.y+ly); } }
        Vector2Int RotateVector(Vector2Int v, int a) { if(a==90)return new Vector2Int(v.y,-v.x); if(a==180)return new Vector2Int(-v.x,-v.y); if(a==270)return new Vector2Int(-v.y,v.x); return v; }
        GameObject PickRandomDoor(List<DoorPrefabData> list, bool isCorridor) 
        { 
            if (list == null || list.Count == 0) return null; 
            
            // [조건 필터]
            var candidates = list.Where(d => 
                (isCorridor && d.corridorSpawn) || // 복도면 복도 허용된 것만
                (!isCorridor && d.roomSpawn)       // 방이면 방 허용된 것만
            ).ToList();
            
            if (candidates.Count == 0) return null; 
            
            float t = candidates.Sum(d => d.weight); 
            float r = CoreRandom.Value() * t, s = 0; 
            foreach (var d in candidates) { s += d.weight; if (r <= s) return d.prefab; } 
            return candidates.Last().prefab; 
        }        
        GameObject GetRandomFloorPrefab() { if(!profile.corridorFloorBasic)return null; if(profile.corridorFloorDecos!=null&&profile.corridorFloorDecos.Count>0&&CoreRandom.Value()<profile.floorDecoChance) return profile.corridorFloorDecos[CoreRandom.Range(0,profile.corridorFloorDecos.Count)]; return profile.corridorFloorBasic; }
        
        GameObject PickRandomCeilingObject(List<CeilingObjectData> list)
        {
            if (list == null || list.Count == 0) return null;
            float total = list.Sum(x => x.weight);
            float r = CoreRandom.Value() * total;
            float s = 0;
            foreach (var item in list) { s += item.weight; if (r <= s) return item.prefab; }
            return list.Last().prefab;
        }


        bool IsValidBounds(RectInt r) => r.x>=0 && r.y>=0 && r.xMax<profile.mapWidth && r.yMax<profile.mapHeight;
        void MarkGrid(RectInt r, int v) { for(int x=r.x;x<r.x+r.width;x++)for(int y=r.y;y<r.y+r.height;y++)grid[x,y]=v; }
        bool IsOverlappingWithSpacing(RectInt r, int spacing)
        {
            int xMin = Mathf.Max(0, r.x - spacing);
            int xMax = Mathf.Min(profile.mapWidth, r.xMax + spacing);
            int yMin = Mathf.Max(0, r.y - spacing);
            int yMax = Mathf.Min(profile.mapHeight, r.yMax + spacing);

            for (int x = xMin; x < xMax; x++)
            {
                for (int y = yMin; y < yMax; y++)
                {
                    if (!IsValidBounds(new RectInt(x, y, 1, 1))) return true; // 맵 밖이면 겹침 판정
                    if (grid[x, y] != 0) return true;
                }
            }
            return false;
        }

        bool IsStrictlyOverlapping(RectInt r) { foreach(var o in placedRooms) if(o.bounds.Overlaps(r)) return true; return false; }
        RoomInstance GetRoomAt(Vector2Int pos) { foreach (var r in placedRooms) if (pos.x >= r.bounds.x && pos.x < r.bounds.xMax && pos.y >= r.bounds.y && pos.y < r.bounds.yMax) return r; return null; }
        bool CheckRoomConnectionStrict(Vector2Int myPos, Vector2Int targetPos, Vector2Int requiredDir) {
            RoomInstance neighbor = GetRoomAt(targetPos); if (neighbor == null) return false;
            foreach (var s in neighbor.mySockets) if (s.gridPos == targetPos && s.forward == requiredDir) return true;
            return false;
        }        
        bool HasNeighborCorridor(Vector2Int p) { Vector2Int[] d={Vector2Int.up,Vector2Int.down,Vector2Int.left,Vector2Int.right}; foreach(var v in d) { Vector2Int n=p+v; if(IsValidBounds(new RectInt(n.x,n.y,1,1)) && grid[n.x,n.y]==2) return true; } return false; }
        void ProcessSocketWallAndFloor(Vector2Int pos, Vector2Int dir, GameObject floorPrefab, Transform corridorRoot, float u) {
            grid[pos.x, pos.y] = 3; Vector2Int front = pos + dir; EnsureCorridorFloorExists(front, floorPrefab, corridorRoot);
            if (tileMap.TryGetValue(pos, out MapTile tile)) {
                Vector3 center = new Vector3(pos.x * u + u * 0.5f, currentFloorHeight, pos.y * u + u * 0.5f);
                Vector3 wallPos = center + new Vector3(dir.x, 0, dir.y) * (u * 0.5f);
                FindAndDisableSocket(tile, wallPos);
            }

            // 3. 반대편(맞닿은 방) 벽 무조건 뚫기
            // 맞은편에 타일이 존재한다면(방이든 복도든), 그 타일의 벽을 찾아서 뚫습니다.
            if (tileMap.TryGetValue(front, out MapTile frontTile))
            {
                // 맞은편 타일의 중심
                Vector3 frontCenter = new Vector3(front.x * u + u * 0.5f, currentFloorHeight, front.y * u + u * 0.5f);
                
                // 맞은편 타일 입장에서 '나'를 바라보는 벽의 위치 (방향이 반대여야 함: -dir)
                Vector3 oppositeWallPos = frontCenter + new Vector3(-dir.x, 0, -dir.y) * (u * 0.5f);
                
                FindAndDisableSocket(frontTile, oppositeWallPos);
            }
        }
    }
}