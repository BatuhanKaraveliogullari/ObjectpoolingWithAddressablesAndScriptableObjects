using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

[CreateAssetMenu(fileName = "NewObjectPooler", menuName = "ObjectPooling/Pooler")]
public class Pooler : ScriptableObject
{
    [Tooltip("Addressableslara kaydedilmiş olan ve bu poolun objesi olan asset")]
    [SerializeField] private AssetReference pooledAsset;

    [Tooltip("Bu asset kullanılmıyorken ne kadar süre sonra release edileceğini gösteren değer. Eğer bulet gibi sık kullanılan bir asset varsa bu süre çok olmalıdır.")]
    [SerializeField] private float releaseAssetDuration;

    private Dictionary<PooledItem,AsyncOperationHandle<GameObject>> instantiatedObjects = new Dictionary<PooledItem,AsyncOperationHandle<GameObject>>();

    private Dictionary<PooledItem,CancellationTokenSource> cancellationTokenSources = new Dictionary<PooledItem, CancellationTokenSource>();

    private Task releaseTask;

    ///<Summary> Position, rotation ve parent önemsenmeksizin spawn işlemi yapılmaktadır.
    /// Spawn edilecek objenin position değeri 0,0,0 dır, rotation değeri identitydir ve parenti yoktur.. </Summary>
    public void Spawn()
    {
        PooledItem pooledItem = GetAvaiableItemInPool();
        if(instantiatedObjects.Count > 0 && pooledItem != null && !pooledItem.gameObject.activeInHierarchy)
        {
            //Release olmayı bekliyen objelerin taski iptal eilecek ve respawn edilecekler.
            Respawn(pooledItem, Vector3.zero, Quaternion.identity, null);
            return;
        }
        //Queue daki object active veya queue da object yok.
        pooledAsset.InstantiateAsync(Vector3.zero, Quaternion.identity, null).Completed += (async) => OnAssetInstantiated(async);
    }

    ///<Summary> Spawn edilecek objenin sadece parenti bellidir.
    /// Spawn edilecek objenin position değeri 0,0,0 dır, rotation değeri identitydir. </Summary>
    public void Spawn(Transform parent)
    {
        PooledItem pooledItem = GetAvaiableItemInPool();
        if(instantiatedObjects.Count > 0 && !pooledItem.gameObject.activeInHierarchy)
        {
            //Release olmayı bekliyen objelerin taski iptal eilecek ve respawn edilecekler.
            Respawn(pooledItem, Vector3.zero, Quaternion.identity, parent);
            return;      
        }

        pooledAsset.InstantiateAsync(Vector3.zero, Quaternion.identity, parent).Completed += (async) => OnAssetInstantiated(async);
    }

    ///<Summary> Position ve rotation değerleriyle spawn işlemi yapılmaktadır. Parent dahil değildir. 
    /// Obje World de belirtilen postion ve rotaion ile parentsız bir şekilde instantiate edilir. </Summary>
    public void Spawn(Vector3 position, Quaternion rotation)
    {
        PooledItem pooledItem = GetAvaiableItemInPool();
        if(instantiatedObjects.Count > 0 && !pooledItem.gameObject.activeInHierarchy)
        {
            //Release olmayı bekliyen objelerin taski iptal eilecek ve respawn edilecekler.
            Respawn(pooledItem, position, rotation, null);
            return;
        }

        pooledAsset.InstantiateAsync(position, rotation, null).Completed += (async) => OnAssetInstantiated(async);
    }

    ///<Summary> Spawn edilecek objenin position, rotaion ve parenti bellidir. Oraya spawn edilir. </Summary>
    public void Spawn(Vector3 position, Quaternion rotation, Transform parent)
    {
        PooledItem pooledItem = GetAvaiableItemInPool();
        if(instantiatedObjects.Count > 0 && !pooledItem.gameObject.activeInHierarchy)
        {
            //Release olmayı bekliyen objelerin taski iptal eilecek ve respawn edilecekler.
            Respawn(pooledItem, position, rotation, parent);
            return;
        }

        pooledAsset.InstantiateAsync(position, rotation, parent).Completed += (async) => OnAssetInstantiated(async);
    }

    ///<Summary> Spawn işlemi bittikten sonra çağırılacak fonksiyon.
    /// Spawn edildikten sonra instantiatedObject listesine dahil etme işlemi gerçekleştirilir. </Summary>
    private void OnAssetInstantiated(AsyncOperationHandle<GameObject> handle)
    {
        if (handle.Result.GetComponent<PooledItem>() == null)
        {
            Debug.LogError("PooledItem which is in addressables does not have PooledItem component");
            return;
        }

        instantiatedObjects.Add(handle.Result.GetComponent<PooledItem>(),handle);
        cancellationTokenSources.Add(handle.Result.GetComponent<PooledItem>(),new CancellationTokenSource());
        handle.Result.GetComponent<PooledItem>().onReturnToPool += ReturnToPool;
    }

    ///<Summary> Respawn Methodu bir obje eğer bundleına geri gönderilmediyse yani release edilmediyse, 
    /// o objeyi tekrar kallanmak için çağırılacak fonksiyon. </Summary>
    private void Respawn(PooledItem itemToRespawn,Vector3 position, Quaternion rotation, Transform parent)
    {
        if(cancellationTokenSources[itemToRespawn] != null)
        {
            cancellationTokenSources[itemToRespawn].Cancel();
        }

        cancellationTokenSources[itemToRespawn] = new CancellationTokenSource();

        Transform itemTransform = itemToRespawn.transform;

        itemTransform.position = position;
        itemTransform.rotation = rotation;
        itemTransform.SetParent(parent);
        itemToRespawn.gameObject.SetActive(true);
    }

    ///<Summary> Bu method sayesinde pollda kullanılmak için aktif olan objeler dündürülmektedir.
    /// Hali hazırda kullanılan objeler döndürülmez. Sadece scenede bulunana ve deactive olan objeler döndürülür. </Summary>
    private PooledItem GetAvaiableItemInPool()
    {
        List<AsyncOperationHandle<GameObject>> tempList = instantiatedObjects.Values.ToList();
        foreach(AsyncOperationHandle<GameObject> pooledItem in tempList)
        {
            if(!pooledItem.Result.activeInHierarchy)
            {
                return pooledItem.Result.GetComponent<PooledItem>();
            }
        }

        return null;
    }

    ///<Summary> Bu method objenin poola dönmesini ve eğer ihtiyaç duyulmaz ise tamamen bundleına gönderilmesini sağlar. </Summary>
    private void ReturnToPool(PooledItem itemToReturn)
    {
        itemToReturn.gameObject.SetActive(false);
        //Release etme taski başlayacak
        releaseTask = ReleaseAsset(itemToReturn, cancellationTokenSources[itemToReturn].Token);
    }

    ///<Summary> Bu method objenin belirli bir süre sonunda bundlea gönderilmesini sağlamaktadır.
    /// Eğer bu thread de yapılan işlemleri cancel edecek bir durum oluşursa bu method yarım kalacaktır. </Summary>
    private async Task ReleaseAsset(PooledItem itemToReturn, CancellationToken cancellationToken)
    {
        Debug.Log("ReleaseAsset");
        try
        {
            Debug.Log("TryRelease");
            await Task.Run(() => WaitForRelease(5f,cancellationToken));

            Addressables.Release(instantiatedObjects[itemToReturn]);

            instantiatedObjects.Remove(itemToReturn);

            cancellationTokenSources.Remove(itemToReturn);
        }
        catch(OperationCanceledException)
        {
            Debug.Log("TaskCanceledException");
        }
    }

    ///<Summary> Bu method assetin veya poollanan objenin bundleına gönderilmesi için gereken bekleme süresini hesap etmektedir. </Summary>
    private void WaitForRelease(float duration, CancellationToken token)
    {
        Debug.Log("waitForRelease");
        for(int i = 0; i < duration; i++)
        {
            Thread.Sleep(1000);
            Debug.Log("Releasing");
            if(token.IsCancellationRequested) 
            {
                token.ThrowIfCancellationRequested();
            }
        }
    }
 
    private void OnDisable() 
    {
        instantiatedObjects.Clear();
        cancellationTokenSources.Clear();
    }
} 
