using UnityEngine;
using LightPath.Core;
using LightPath.Utils;
using TMPro;
using UnityEngine.SceneManagement;
using LightPath.MapGen;
using System;
using NUnit.Framework;

public class SeedViewer : MonoBehaviour
{
    public GameObject mapComponent;
    private MapGenerator map;
    
    // 디버깅? 테스트용 스크립트
    public TextMeshProUGUI seednum;
    public TextMeshProUGUI elapsedTimeText;

    void Start()
    {
        seednum.text = $"현재 시드 : {CoreRandom.CurrentSeed}";
        map = mapComponent.GetComponent<MapGenerator>();
    }


    void Update()
    {   
        if (map!=null)
        {
            elapsedTimeText.text = $"{map.elapsedTime}";  
        }


    }

    public void OnclickBtn()
    {
        SceneManager.LoadScene("MainGame");
    }

}
