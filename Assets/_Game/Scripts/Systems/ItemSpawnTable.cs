using UnityEngine;
using System.Collections.Generic;

namespace LightPath.Systems
{
    [System.Serializable]
    public struct ItemEntry
    {
        public ItemData item; // 어떤 아이템인지
        public int count;     // 통 안에 몇 개 넣을지 (예: 양초 3개, 열쇠 1개)
    }

    /// <summary>
    /// 챕터/난이도 별로 아이템 등장 확률과 구성을 정의하는 설정 파일
    /// </summary>
    [CreateAssetMenu(fileName = "SpawnTable_Ch1_Normal", menuName = "LightPath/Spawn Table")]
    public class ItemSpawnTable : ScriptableObject
    {
        [Header("설정")]
        [Range(0f, 1f)]
        public float spawnChance = 0.5f; // 서랍을 열었을 때 아이템이 나올 확률 (0% ~ 100%)

        [Header("제비 뽑기 리스트 구성")]
        public List<ItemEntry> initialPool; // 이 구성대로 통(Bag)을 채웁니다.
    }
}