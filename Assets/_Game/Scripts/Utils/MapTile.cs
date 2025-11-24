using UnityEngine;

namespace LightPath.MapGen
{
    // 타일에 내릴 수 있는 명령 종류
    public enum TileAction 
    { 
        Break,      // 부서짐 (바닥)
        Corrupt,    // 썩음/오염 (벽, 바닥)
        Deactivate, // 비활성화/닫힘 (제단)
        Unlock      // 잠금 해제 (문)
    }

    public class MapTile : MonoBehaviour
    {
        [Header("설정")]
        public bool isBreakable = false; // 부서질 수 있는가?
        public GameObject brokenPrefab;  // 부서졌을 때 교체될 프리팹

        [Header("애니메이션 (선택)")]
        public Animator animator;        // 애니메이션으로 처리할 경우 (예: 제단 닫힘)
        public string corruptTrigger = "OnCorrupt";
        public string deactivateTrigger = "OnClose";

        // 상태 변경 요청을 받는 함수
        public void OnReceiveCommand(TileAction action)
        {
            switch (action)
            {
                case TileAction.Break:
                    if (isBreakable && brokenPrefab != null)
                    {
                        // 1. 프리팹 교체 방식
                        // 기존 위치/회전 저장
                        Vector3 pos = transform.position;
                        Quaternion rot = transform.rotation;
                        Transform parent = transform.parent;

                        // 나 자신 삭제
                        Destroy(gameObject);

                        // 부서진 버전 생성
                        GameObject go = Instantiate(brokenPrefab, pos, rot, parent);
                        
                        // [중요] 새로 생긴 애도 MapTile을 가지고 있다면, 매니저에 갱신 요청해야 함
                        // (이 부분은 MapManager가 자동으로 처리하도록 설계 가능)
                    }
                    break;

                case TileAction.Corrupt:
                    // 2. 애니메이션/메터리얼 변경 방식
                    if (animator != null) animator.SetTrigger(corruptTrigger);
                    // 또는 Renderer를 가져와서 텍스처 교체 로직 수행
                    break;

                case TileAction.Deactivate:
                    // 3. 제단 닫힘
                    if (animator != null) animator.SetTrigger(deactivateTrigger);
                    break;
            }
        }
    }
}