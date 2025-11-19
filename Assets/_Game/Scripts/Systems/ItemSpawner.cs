using UnityEngine;
using System.Collections.Generic;
using LightPath.Utils; 

namespace LightPath.Systems
{
    public class ItemSpawner : MonoBehaviour
    {
        public ItemSpawnTable currentTable; // 챕터/난이도별 확률표

        [Header("보정 설정")]
        public float failBonusRate = 0.05f; // 실패 시 증가할 확률
        
        [SerializeField]
        private float currentBonusChance = 0f; // 현재 누적된 보정치

        private List<ItemData> currentBag = new List<ItemData>();

        // 맵 생성 직후 또는 로드시 호출
        public void InitializeBag()
        {
            currentBonusChance = 0f;
            RefillBag();
        }

        /// <summary>
        /// 서랍을 열 때 호출. 아이템 획득 여부와 종류를 반환합니다.
        /// </summary>
        public ItemData TrySpawnItem()
        {
            float finalChance = currentTable.spawnChance + currentBonusChance;
            
            // 1. 생성 여부 판별 (CoreRandom 사용)
            if (CoreRandom.Value() > finalChance)
            {
                // 꽝! 보정치 증가 (다음엔 나올 확률 높아짐)
                currentBonusChance += failBonusRate; 
                return null;
            }

            // 당첨! 보정치 초기화
            currentBonusChance = 0f; 

            // 2. 가방이 비었으면 리필
            if (currentBag.Count == 0)
            {
                RefillBag();
            }

            // 3. 가방에서 랜덤으로 하나 뽑기 (중복 방지)
            int randomIndex = CoreRandom.Range(0, currentBag.Count);
            ItemData pickedItem = currentBag[randomIndex];
            currentBag.RemoveAt(randomIndex);

            return pickedItem;
        }

        private void RefillBag()
        {
            currentBag.Clear();
            // 테이블에 설정된 개수만큼 가방에 채워 넣기
            foreach (var entry in currentTable.initialPool)
            {
                for (int i = 0; i < entry.count; i++)
                {
                    currentBag.Add(entry.item);
                }
            }
        }
    }
}