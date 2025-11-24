using UnityEngine;
using LightPath.Utils; 

namespace LightPath.Core
{
    public enum GameState { Title, InGame, Result }

    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance;

        [Header("글로벌 설정")]
        [Tooltip("이 시드 하나로 게임 내 모든 랜덤 요소(맵, 아이템, AI)가 결정됩니다.")]
        public int globalSeed = 1234; 
        
        [Tooltip("체크 시, 게임 시작마다 시간 기반의 새로운 시드를 생성합니다.")]
        public bool useRandomSeedOnPlay = false; 

        public GameState CurrentState { get; private set; }

        private void Awake()
        {
            // 싱글톤 초기화
            if (Instance == null) 
            { 
                Instance = this; 
                DontDestroyOnLoad(gameObject); 
            }
            else 
            { 
                Destroy(gameObject);
                return;
            }

            // [핵심] 시드 초기화
            // 리플레이나 특정 상황 재현을 원할 경우 useRandomSeedOnPlay를 끄고 globalSeed를 고정합니다.
            if (useRandomSeedOnPlay)
            {
                globalSeed = Random.Range(1, 2147483647);
            }
            
            CoreRandom.Initialize(globalSeed);
            Debug.Log($"[GameManager] 글로벌 시드 초기화 완료: {globalSeed}");
        }

        public void ChangeState(GameState newState)
        {
            CurrentState = newState;
            // TODO: 상태 변경에 따른 UI 처리나 게임 흐름 제어 로직 추가 예정
        }
    }
}