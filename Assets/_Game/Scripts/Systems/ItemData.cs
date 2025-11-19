using UnityEngine;

namespace LightPath.Systems
{
    [CreateAssetMenu(fileName = "NewItem", menuName = "LightPath/Item Data")]
    public class ItemData : ScriptableObject
    {
        public string itemName;        // 아이템 이름
        public GameObject prefab;      // 생성될 프리팹
        [TextArea]
        public string description;     // 설명
    }
}