using UnityEngine;

namespace LightPath.Utils
{
    public class RoomSocket : MonoBehaviour
    {
        [Header("Door Settings")]
        [Tooltip("체크 시, 조건(복도 2칸 확보)이 맞으면 '무조건' 미닫이문(2칸)을 생성합니다.")]
        public bool isSlidingCandidate = false;

        [Tooltip("이 소켓 위치에 이미 특수한 문을 배치했나요? (체크하면 생성기가 건드리지 않음)")]
        public bool hasPreplacedDoor = false;

        // 미닫이문의 너비 (보통 2칸이라 가정, 유닛 단위에 맞게 수정 필요)
        private const float DOOR_WIDTH = 2.0f; 
        // 복도 탐지 거리
        private const float CHECK_DISTANCE = 2.0f; 

        private void OnDrawGizmos()
        {
            // 1. 기본 소켓 위치 표시
            Gizmos.color = hasPreplacedDoor ? Color.red : (isSlidingCandidate ? Color.cyan : Color.green);
            Gizmos.DrawWireSphere(transform.position, 0.3f);

            // 2. 미닫이문 후보일 경우, 필요한 공간(2칸)을 시각화
            if (isSlidingCandidate && !hasPreplacedDoor)
            {
                // 기준점(문 왼쪽) 앞의 복도
                Vector3 frontPos1 = transform.position + transform.forward * CHECK_DISTANCE;
                // 확장점(문 오른쪽) 앞의 복도 (문이 오른쪽으로 확장된다고 가정)
                Vector3 frontPos2 = transform.position + transform.right * DOOR_WIDTH + transform.forward * CHECK_DISTANCE;

                // 복도 연결 라인 그리기
                Gizmos.color = Color.yellow;
                Gizmos.DrawLine(transform.position, frontPos1);
                Gizmos.DrawLine(transform.position + transform.right * DOOR_WIDTH, frontPos2);

                // 두 복도 지점을 연결하여 "진입로"가 확보되었는지 표현
                Gizmos.DrawLine(frontPos1, frontPos2);
            }
            else
            {
                // 일반 문은 한 줄만 표시
                Gizmos.color = Color.blue;
                Gizmos.DrawLine(transform.position, transform.position + transform.forward * 2f);
            }
        }
        
        /// <summary>
        /// 실제 생성기(MapGenerator)에서 호출할 검증 함수입니다.
        /// 미닫이문(2칸)을 설치하기에 적합한 복도 공간이 있는지 확인합니다.
        /// </summary>
        /// <param name="corridorLayerMask">복도 바닥이나 벽을 감지할 레이어 마스크</param>
        public bool IsValidForSlidingDoor(LayerMask corridorLayerMask)
        {
            if (!isSlidingCandidate) return false;

            // 문 바로 앞 (1번 칸)
            Vector3 checkPos1 = transform.position + transform.forward * 1.0f; // 거리는 타일 크기에 맞춰 조정 (예: 1.0f)
            
            // 문 옆 칸의 앞 (2번 칸 - 오른쪽으로 확장된다고 가정)
            Vector3 checkPos2 = transform.position + transform.right * DOOR_WIDTH + transform.forward * 1.0f;

            // 두 위치 모두에 복도(또는 빈 공간)가 있는지 레이캐스트나 오버랩으로 확인
            // 여기서는 예시로 CheckSphere를 사용합니다. (실제 프로젝트의 타일 판정 방식에 맞춰 수정 필요)
            bool hasSpace1 = Physics.CheckSphere(checkPos1, 0.4f, corridorLayerMask);
            bool hasSpace2 = Physics.CheckSphere(checkPos2, 0.4f, corridorLayerMask);

            // 두 칸 모두 공간이 있어야 true
            return hasSpace1 && hasSpace2;
        }
    }
}