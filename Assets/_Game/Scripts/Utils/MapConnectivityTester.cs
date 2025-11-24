using UnityEngine;
using UnityEngine.AI;
using System.Collections;
using System.Collections.Generic;
using LightPath.MapGen;

namespace LightPath.Utils
{
    public class MapConnectivityTester : MonoBehaviour
    {
        [Header("Settings")]
        [Range(10, 100)] public float speed = 30f; // 공의 속도
        public float waitTime = 0.2f; // 방 도착 후 대기 시간
        public Material ballMat;      // 공 색깔 (없으면 기본)
        public bool useSmartPathing = true;
        
        [Header("Debug Info")]
        public int totalRooms;
        public int visitedRooms;
        public List<string> unreachableRooms = new List<string>();

        private NavMeshAgent agent;
        private List<MapGenerator.RoomInstance> targetRooms = new List<MapGenerator.RoomInstance>();
        private Vector3 startPos;

        // 맵 생성이 끝나면 외부에서 이 함수를 호출해주세요.
        public void StartTest(List<MapGenerator.RoomInstance> rooms)
        {
            StopAllCoroutines();
            
            // 원본 리스트 복사
            List<MapGenerator.RoomInstance> rawRooms = new List<MapGenerator.RoomInstance>(rooms);

            // 1. 시작 방 찾기
            var startRoom = rawRooms.Find(r => r.currentType == RoomType.Start);
            if (startRoom == null) startRoom = rawRooms[0];
            
            float u = MapGenerator.Instance.profile.unitSize;
            startPos = new Vector3(startRoom.Center.x * u, 0, startRoom.Center.y * u);

            // 2. [핵심] 경로 최적화 (스마트 정렬)
            if (useSmartPathing)
            {
                // 시작 방은 리스트에서 빼고, 시작 위치 기준으로 가까운 순서대로 줄 세우기
                rawRooms.Remove(startRoom);
                targetRooms = SortRoomsSmart(rawRooms, startPos);
            }
            else
            {
                // 기존 방식 (생성된 순서 혹은 단순 거리순)
                targetRooms = rawRooms;
            }

            // 3. 공 생성
            CreateExplorerBall(startPos);

            // 4. 순찰 시작
            StartCoroutine(PatrolRoutine());
        }

        // [새로 추가] 가장 가까운 방을 찾아가는 'Nearest Neighbor' 알고리즘
        List<MapGenerator.RoomInstance> SortRoomsSmart(List<MapGenerator.RoomInstance> unsorted, Vector3 currentPos)
        {
            List<MapGenerator.RoomInstance> sorted = new List<MapGenerator.RoomInstance>();
            List<MapGenerator.RoomInstance> pool = new List<MapGenerator.RoomInstance>(unsorted);
            float u = MapGenerator.Instance.profile.unitSize;

            while (pool.Count > 0)
            {
                MapGenerator.RoomInstance nearest = null;
                float minDst = float.MaxValue;

                // 남은 방들 중에서 내 현재 위치(currentPos)와 제일 가까운 녀석 찾기
                foreach (var r in pool)
                {
                    Vector3 rPos = new Vector3(r.Center.x * u, 0, r.Center.y * u);
                    // 실제 경로 거리는 NavMeshPath를 써야 정확하지만, 
                    // 연산 비용상 직선거리로만 비교해도 충분히 효율적입니다.
                    float d = Vector3.SqrMagnitude(currentPos - rPos);

                    if (d < minDst)
                    {
                        minDst = d;
                        nearest = r;
                    }
                }

                if (nearest != null)
                {
                    sorted.Add(nearest);
                    pool.Remove(nearest);
                    // 내 위치를 방금 찾은 방으로 갱신 (거기서 또 다음 가까운 방을 찾음)
                    currentPos = new Vector3(nearest.Center.x * u, 0, nearest.Center.y * u);
                }
            }

            return sorted;
        }
        void CreateExplorerBall(Vector3 pos)
        {
            if (agent != null) Destroy(agent.gameObject);

            GameObject ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            ball.name = "나는 공이예요";
            
            // 물리 충돌체 제거 (필수)
            Destroy(ball.GetComponent<Collider>()); 

            // 위치 보정 (NavMesh 위로 안착)
            if (NavMesh.SamplePosition(pos, out NavMeshHit hit, 5.0f, NavMesh.AllAreas))
            {
                ball.transform.position = hit.position;
            }
            else
            {
                ball.transform.position = pos;
            }
            
            ball.transform.localScale = Vector3.one * 1.0f; // 크기도 1.0으로 적당히 조절

            if (ballMat) ball.GetComponent<Renderer>().material = ballMat;
            else ball.GetComponent<Renderer>().material.color = Color.cyan;

            agent = ball.AddComponent<NavMeshAgent>();
            
            // [핵심 수정] 관성 제거 설정
            agent.speed = speed;
            agent.acceleration = 999999f; // 거의 무한대 (즉시 최고 속도 도달)
            agent.angularSpeed = 999999f; // 즉시 회전 (딜레이 없음)
            agent.stoppingDistance = 0.1f; // 목표에 아주 가까이 가야 멈춤
            agent.autoBraking = false;    // 도착 전 감속(끼익~) 끄기. 팍! 하고 멈춤.
            agent.radius = 0.2f;          // 홀쭉하게

            // Trail 설정
            TrailRenderer trail = ball.AddComponent<TrailRenderer>();
            trail.time = 3.0f;
            trail.startWidth = 0.5f;
            trail.endWidth = 0.0f;
            trail.material = new Material(Shader.Find("Sprites/Default"));
            trail.startColor = Color.cyan;
            trail.endColor = new Color(0, 1, 1, 0);
        }

        IEnumerator PatrolRoutine()
        {
            // [수정 1] NavMesh가 준비될 때까지 잠시 대기 (안정성 확보)
            yield return new WaitForEndOfFrame(); 
            yield return new WaitForEndOfFrame();

            // 시작점 강제 안착 (Warp)
            if (NavMesh.SamplePosition(startPos, out NavMeshHit hit, 5.0f, NavMesh.AllAreas))
            {
                agent.Warp(hit.position);
            }
            else
            {
                Debug.LogError("[Tester] 시작 지점에 NavMesh가 없습니다! (Bake 실패 또는 벽 속)");
                yield break;
            }

            totalRooms = targetRooms.Count;
            visitedRooms = 0;
            unreachableRooms.Clear();

            Debug.Log(">>> [Tester] 탐색 시작!");

            NavMeshPath path = new NavMeshPath();

            foreach (var room in targetRooms)
            {
                float u = MapGenerator.Instance.profile.unitSize;
                Vector3 targetPos = new Vector3(room.Center.x * u, 0, room.Center.y * u);

                // [수정 2] 경로 계산 시도
                if (agent.CalculatePath(targetPos, path) && path.status == NavMeshPathStatus.PathComplete)
                {
                    // 경로 따라 이동 (GPS 모드)
                    for (int i = 1; i < path.corners.Length; i++)
                    {
                        Vector3 nextPoint = path.corners[i];
                        
                        // 무한 루프 방지용 타임아웃
                        float timeOut = 5.0f; 
                        while (Vector3.Distance(transform.position, nextPoint) > 0.1f && timeOut > 0)
                        {
                            transform.position = Vector3.MoveTowards(transform.position, nextPoint, speed * Time.deltaTime);
                            timeOut -= Time.deltaTime;
                            yield return null;
                        }
                    }
                    visitedRooms++;
                }
                else
                {
                    Debug.LogError($"[Tester] ❌ 고립됨: {room.spawnedObject.name}");
                    unreachableRooms.Add(room.spawnedObject.name);
                    Debug.DrawLine(transform.position, targetPos, Color.red, 100f); // 빨간 줄 긋기
                }
                
                yield return null; // 너무 빠르면 렉 걸리니 1프레임 휴식
            }
            
            // (복귀 로직 생략 - 위와 동일하게 처리하면 됨)
            Debug.Log(">>> [Tester] 테스트 종료.");
        }
                    

    }
}