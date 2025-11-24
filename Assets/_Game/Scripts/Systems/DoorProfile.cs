using UnityEngine;
using System.Collections.Generic;

namespace LightPath.Systems
{
    [CreateAssetMenu(fileName = "NewDoorProfile", menuName = "LightPath/Door Profile")]
    public class DoorProfile : ScriptableObject
    {
        [Header("기본 스펙")]
        public float maxHP = 100f;
        public bool ignoreForceOpen = false; // 결계 등은 true
        public float maxStability = 50f;     // 강제 개방 저항력

        [Header("파괴 효과 (공유 리소스)")]
        [Tooltip("파괴 시 생성될 물리 파편 프리팹")]
        public GameObject physicsDebrisPrefab; 
        public float debrisLifetime = 5f;
        
        [Tooltip("타격 시 발생하는 이펙트")]
        public GameObject hitFX; 

        [Tooltip("파괴 후 남을 흔적들 (랜덤 선택)")]
        public List<GameObject> destroyedTraces; 
    }
}