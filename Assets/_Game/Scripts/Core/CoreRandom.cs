using UnityEngine;

namespace LightPath.Utils
{
    /// <summary>
    /// 완전 결정론적 난수 생성기.
    /// 시드 하나가 맵, 아이템, AI 등 모든 확률을 통제하여 완벽한 리플레이를 보장합니다.
    /// </summary>
    public static class CoreRandom
    {
        private static System.Random seedGen;
        
        // 현재 적용된 시드값 (읽기 전용)
        public static int CurrentSeed { get; private set; } 

        /// <summary>
        /// 게임 시작 시(GameManager) 딱 한 번 호출하여 시드를 심습니다.
        /// </summary>
        public static void Initialize(int seed)
        {
            CurrentSeed = seed;
            seedGen = new System.Random(seed);
            Debug.Log($"[CoreRandom] System Initialized. Seed: {seed}");
        }

        /// <summary>
        /// 정수 범위 랜덤 (min 포함, max 제외)
        /// </summary>
        public static int Range(int min, int max)
        {
            if (seedGen == null) Initialize(System.Environment.TickCount); // 안전장치
            return seedGen.Next(min, max);
        }
        
        /// <summary>
        /// 0.0 ~ 1.0 사이의 실수 반환 (확률 계산용)
        /// </summary>
        public static float Value()
        {
            if (seedGen == null) Initialize(System.Environment.TickCount);
            return (float)seedGen.NextDouble();
        }
    }
}