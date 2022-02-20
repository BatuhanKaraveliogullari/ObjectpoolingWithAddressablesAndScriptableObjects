using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

[CreateAssetMenu(fileName = "NewObjectPooler", menuName = "ObjectPooling/Pooler")]
public class Pooler : ScriptableObject
{
    [Tooltip("Addressableslara kaydedilmiş olan ve bu poolun objesi olan asset")]
    [SerializeField] private AssetReference pooledAsset;

    [Tooltip("Bu asset kullanılmıyorken ne kadar süre sonra release edileceğini gösteren değer. Eğer bulet gibi sık kullanılan bir asset varsa bu süre çok olmalıdır.")]
    [Min(1f)] [SerializeField] private int releaseAssetDuration = 1;

    private Dictionary<PooledItem,AsyncOperationHandle<GameObject>> instantiatedObjects = new Dictionary<PooledItem,AsyncOperationHandle<GameObject>>();

    private Dictionary<PooledItem,CancellationTokenSource> cancellationTokenSources = new Dictionary<PooledItem, CancellationTokenSource>();

    private Task releaseTask;

    ///<Summary> Position, rotation ve parent önemsenmeksizin spawn işlemi yapılmaktadır.
    /// Spawn edilecek objenin position değeri 0,0,0 dır, rotation değeri identitydir ve parenti yoktur.. 
    /// NOTE: Async method içerisinde çağırılır. </Summary>
    public async Task<PooledItem> Spawn()
    {
        PooledItem pooledItem = GetAvaiableItemInPool();
        if(instantiatedObjects.Count > 0 && pooledItem != null && !pooledItem.gameObject.activeInHierarchy)
        {
            //Release olmayı bekliyen objelerin taski iptal eilecek ve respawn edilecekler.
            Respawn(pooledItem, Vector3.zero, Quaternion.identity, null);
            return pooledItem;
        }
        //Queue daki object active veya queue da object yok.
        pooledItem = await SpawnAsync(Vector3.zero, Quaternion.identity, null);
        return pooledItem;
        //pooledAsset.InstantiateAsync(Vector3.zero, Quaternion.identity, null).Completed += (async) => OnAssetInstantiated(async);
    }

    ///<Summary> Spawn edilecek objenin sadece parenti bellidir.
    /// Spawn edilecek objenin position değeri 0,0,0 dır, rotation değeri identitydir.
    /// NOTE: Async method içerisinde çağırılır. </Summary>
    public async Task<PooledItem> Spawn(Transform parent)
    {
        PooledItem pooledItem = GetAvaiableItemInPool();
        if(instantiatedObjects.Count > 0 && !pooledItem.gameObject.activeInHierarchy)
        {
            //Release olmayı bekliyen objelerin taski iptal eilecek ve respawn edilecekler.
            Respawn(pooledItem, Vector3.zero, Quaternion.identity, parent);
            return pooledItem;      
        }

        //Queue daki object active veya queue da object yok.
        pooledItem = await SpawnAsync(Vector3.zero, Quaternion.identity, parent);
        return pooledItem;
    }

    ///<Summary> Position ve rotation değerleriyle spawn işlemi yapılmaktadır. Parent dahil değildir. 
    /// Obje World de belirtilen postion ve rotaion ile parentsız bir şekilde instantiate edilir.
    /// NOTE: Async method içerisinde çağırılır. </Summary>
    public async Task<PooledItem> Spawn(Vector3 position, Quaternion rotation)
    {
        PooledItem pooledItem = GetAvaiableItemInPool();
        if(instantiatedObjects.Count > 0 && !pooledItem.gameObject.activeInHierarchy)
        {
            //Release olmayı bekliyen objelerin taski iptal eilecek ve respawn edilecekler.
            Respawn(pooledItem, position, rotation, null);
            return pooledItem;
        }
        //Queue daki object active veya queue da object yok.
        pooledItem = await SpawnAsync(position, rotation, null);
        return pooledItem;
    }

    ///<Summary> Spawn edilecek objenin position, rotaion ve parenti bellidir. Oraya spawn edilir.
    /// NOTE: Async method içerisinde çağırılır. </Summary>
    public async Task<PooledItem> Spawn(Vector3 position, Quaternion rotation, Transform parent)
    {
        PooledItem pooledItem = GetAvaiableItemInPool();
        if(instantiatedObjects.Count > 0 && !pooledItem.gameObject.activeInHierarchy)
        {
            //Release olmayı bekliyen objelerin taski iptal eilecek ve respawn edilecekler.
            Respawn(pooledItem, position, rotation, parent);
            return pooledItem;
        }
        //Queue daki object active veya queue da object yok.
        pooledItem = await SpawnAsync(position, rotation, parent);
        return pooledItem;
    }

    private async Task<PooledItem> SpawnAsync(Vector3 position, Quaternion rotation, Transform parent)
    {
        AsyncOperationHandle<GameObject> aws = pooledAsset.InstantiateAsync(position, rotation, parent);
        Task asd = aws.Task;
        await asd;
        OnAssetInstantiated(aws);
        return aws.Result.GetComponent<PooledItem>();
    }

    ///<Summary> Spawn işlemi bittikten sonra çağırılacak fonksiyon.
    /// Spawn edildikten sonra instantiatedObject listesine dahil etme işlemi gerçekleştirilir. </Summary>
    private void OnAssetInstantiated(AsyncOperationHandle<GameObject> handle)
    {
        Assert.IsNotNull(handle.Result, "PooledItem which is in addressables does not have PooledItem component");
        if (handle.Result.GetComponent<PooledItem>() == null) return;
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

        if(tempList.Count > 0)
        {
            if(tempList[0].IsValid())
            {
                if(tempList[0].Result == null)
                {
                    ClearPoolEntirely();
                    return null;
                }
            }
            else
            {
                ClearPoolEntirely();
                return null;
            }
        }

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
        try
        {
            await Task.Run(() => WaitForRelease(releaseAssetDuration,cancellationToken));

            Addressables.Release(instantiatedObjects[itemToReturn]);

            Debug.Log(itemToReturn.name + " released");

            ClearAsset(itemToReturn);
        }
        catch(OperationCanceledException)
        {
            Debug.Log(itemToReturn.name + "'s release canceled to reusage");
        }
    }

    ///<Summary> Bu method assetin veya poollanan objenin bundleına gönderilmesi için gereken bekleme süresini hesap etmektedir. </Summary>
    private void WaitForRelease(float duration, CancellationToken token)
    {
        Debug.Log("Asset will release after " + duration + " seconds");
        for(int i = 0; i < duration; i++)
        {
            Thread.Sleep(1000);
            
            if(token.IsCancellationRequested) 
            {
                token.ThrowIfCancellationRequested();
            }
        }
    }

    private void ClearPoolEntirely()
    {
        instantiatedObjects.Clear();
        cancellationTokenSources.Clear();
    }

    private void ClearAsset(PooledItem pooledItem)
    {
        instantiatedObjects.Remove(pooledItem);
        cancellationTokenSources.Remove(pooledItem);
    }
 
    private void OnDisable() 
    {
        ClearPoolEntirely();
    }
} 