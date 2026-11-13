using System;
using Artel.Protocol.Dto;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Artel
{
    /// <summary>
    /// 등록 시점의 씬 목록을 서버에 보고할 형태로 만든다.
    ///
    /// Build Settings 를 읽을 뿐이라 씬을 로드하지 않는다. 예전에는 <see cref="AllSceneScanner"/>
    /// 로 씬을 하나씩 올렸다 내리며 씬마다 UI 트리를 함께 실어 보냈다(`scannedScenes`). 그 트리는
    /// 서버가 `game_build.scene_scan` 에 저장만 하고 아무도 읽지 않았다 — 읽히는 것은 여기
    /// 목록의 0번 하나뿐이고, 그것은 씬 워크 없이 나온다.
    /// </summary>
    internal static class SceneScanReporter
    {
        /// <summary>
        /// 목록을 못 만들어도 등록을 막을 이유는 없으므로, 읽다 터지면 그때까지 담은 것만 낸다.
        /// </summary>
        public static SceneScanReportDto CreateReport()
        {
            var report = new SceneScanReportDto();
            try
            {
                for (var i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
                {
                    report.ScenesInBuild.Add(SceneUtility.GetScenePathByBuildIndex(i));
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Artel: Build Settings의 씬 목록을 읽지 못했습니다. " + exception.Message);
            }

            return report;
        }
    }
}
