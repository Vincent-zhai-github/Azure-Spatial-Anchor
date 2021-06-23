using Microsoft.Azure.SpatialAnchors;
using Microsoft.Azure.SpatialAnchors.Unity;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace JTASAManager
{
    [RequireComponent(typeof(SpatialAnchorManager))]
    public class ASAManager : MonoBehaviour
    {
        private SpatialAnchorManager cloudManager;
        bool asaStarted = false;
        bool isErrorActive = false;
        CloudSpatialAnchor currentCloudAnchor;
        CloudSpatialAnchorWatcher currentWatcher;
        public GameObject sphere;

        private List<string> anchorIDs = new List<string>();

        private List<GameObject> anchorGO = new List<GameObject>();

        public Text AnchorID;

        private bool isCreate = false;

        private float recommended;

        private int anchorIDNumber = 0;
        // Start is called before the first frame update
        void Start()
        {
            cloudManager = transform.GetComponent<SpatialAnchorManager>();
            cloudManager.SpatialAnchorsAccountId = "Your Account Id";
            cloudManager.SpatialAnchorsAccountDomain = "Your Account Domain";
            cloudManager.SpatialAnchorsAccountKey = "Your Account Key";

            cloudManager.SessionUpdated += CloudManager_SessionUpdated;
            cloudManager.AnchorLocated += CloudManager_AnchorLocated;
            cloudManager.LocateAnchorsCompleted += CloudManager_LocateAnchorsCompleted;
            cloudManager.LogDebug += CloudManager_LogDebug;
            cloudManager.Error += CloudManager_Error;
        }

        private void CloudManager_Error(object sender, SessionErrorEventArgs args)
        {
            isErrorActive = true;

            Debug.Log("ASA error: " + args.ErrorMessage);
        }

        private void CloudManager_LogDebug(object sender, OnLogDebugEventArgs args)
        {
            Debug.Log("ASA Debug: " + args.Message);
        }

        private void CloudManager_LocateAnchorsCompleted(object sender, LocateAnchorsCompletedEventArgs args)
        {
            Debug.Log("All anchors located");
        }

        private void CloudManager_AnchorLocated(object sender, AnchorLocatedEventArgs args)
        {
            switch (args.Status)
            {
                case LocateAnchorStatus.Located:
                    UnityMainThreadDispatcher.Instance().Enqueue(() =>
                    {
                        Debug.Log("ASA Info: Anchor located! Identifier: " + args.Identifier);

                        CloudSpatialAnchor _cloudSpatialAnchor = args.Anchor;

                        GameObject go = Instantiate(sphere);

                        CloudNativeAnchor cloudNativeAnchor = go.AddComponent<CloudNativeAnchor>();
                        cloudNativeAnchor.CloudToNative(_cloudSpatialAnchor);
                        anchorGO.Add(go);
                        anchorIDNumber--;
                        if (anchorIDNumber <= 0)
                        {
                            located.TrySetResult(true);
                        }
                    });
                    break;
                case LocateAnchorStatus.AlreadyTracked:
                    Debug.Log("ASA Info: Anchor already tracked. Identifier: " + args.Identifier);
                    break;
                case LocateAnchorStatus.NotLocated:
                    Debug.Log("ASA Info: Anchor not located. Identifier: " + args.Identifier);
                    break;
                case LocateAnchorStatus.NotLocatedAnchorDoesNotExist:
                    Debug.LogError("ASA Error: Anchor not located does not exist. Identifier: " + args.Identifier);
                    break;
            }
        }

        private void CloudManager_SessionUpdated(object sender, SessionUpdatedEventArgs args)
        {
            Debug.Log("ASA Log: recommendedForCreate: " + args.Status.RecommendedForCreateProgress);
            recommended = args.Status.RecommendedForCreateProgress;
        }

        // Update is called once per frame
        void Update()
        {

        }

        /// <summary>
        /// start session
        /// </summary>
        /// <returns></returns>
        private async Task<bool> startASA()
        {
            if (!asaStarted)
            {
                try
                {
                    await cloudManager.CreateSessionAsync();
                    await cloudManager.StartSessionAsync();
                    asaStarted = true;
                }
                catch (Exception ex)
                {
                    Debug.Log("failed to start ASA " + ex.Message);
                }
            }

            return asaStarted;
        }

        /// <summary>
        /// stop session
        /// </summary>
        private void stopASA()
        {
            if (asaStarted)
            {
                cloudManager.StopSession();
                cloudManager.DestroySession();
                asaStarted = false;
            }
        }

        /// <summary>
        /// 创建Anchor
        /// </summary>
        /// <param name="anchorObj">需要同步位置的物体</param>
        /// <returns></returns>
        public async Task<string> CreateAnchor(GameObject anchorObj)
        {
            bool started = await startASA();
            if (started)
            {
                anchorObj.CreateNativeAnchor();

                CloudSpatialAnchor localCloudAnchor = new CloudSpatialAnchor();

                localCloudAnchor.LocalAnchor = anchorObj.FindNativeAnchor().GetPointer();

                if (localCloudAnchor.LocalAnchor == IntPtr.Zero)
                {
                    return null;
                }

                localCloudAnchor.Expiration = DateTimeOffset.Now.AddDays(7);     //设置创建的cloud anchor 7天有效期

                //等待足够的空间数据
                while (recommended < 1.0f)
                {
                    Debug.Log("wait recommended : " + recommended);
                    await Task.Delay(330);
                }

                bool success = false;

                try
                {
                    // Actually save
                    await cloudManager.CreateAnchorAsync(localCloudAnchor);

                    // Store
                    currentCloudAnchor = localCloudAnchor;
                    localCloudAnchor = null;

                    // Success?
                    success = currentCloudAnchor != null;

                    Debug.Log("anchor create success" + success);

                    if (success && !isErrorActive)
                    {
                        anchorIDs.Add(currentCloudAnchor.Identifier);
                        isCreate = false;
                        return currentCloudAnchor.Identifier;
                    }
                    else
                    {
                        Debug.Log("failed to create asa anchor");
                    }
                }
                catch (Exception ex)
                {
                    Debug.Log("Exception occurred: " + ex.Message);
                }
            }

            return null;
        }


        /// <summary>
        /// 查找Anchor
        /// </summary>
        /// <param name="anchorId">要查找的anchor的AnchorId</param>
        /// <returns></returns>
        public async void SyncAnchor(string[] anchorIds)
        {
            bool started = await startASA();
            if (!started)
            {
                Debug.Log("ASA started failed");
            }

            anchorIDNumber = anchorIds.Length;

            //等待足够的空间数据
            while (recommended < 1.0f)
            {
                Debug.Log("wait recommended : " + recommended);
                await Task.Delay(330);
            }

            try
            {
                if ((cloudManager != null) && (cloudManager.Session != null))
                {
                    var anchorLocateCriteria = new AnchorLocateCriteria();
                    anchorLocateCriteria.Identifiers = anchorIds;

                    //创建一个Watcher来查找anchor
                    currentWatcher = cloudManager.Session.CreateWatcher(anchorLocateCriteria);
                }
            }
            catch (Exception ex)
            {
                Debug.Log("failed sync anchor: " + ex.Message);
            }
        }

        public void LoadAnchors()
        {
            SyncAnchor(anchorIDs.ToArray());
        }

        public void DeleteAnchor(GameObject deleteAnchorGO)
        {
            deleteAnchorGO.DeleteNativeAnchor();
        }
    }
}