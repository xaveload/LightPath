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
    }
}