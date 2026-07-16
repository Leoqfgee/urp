using System;
using UnityEngine;

namespace Urp.ArDemo
{
    [CreateAssetMenu(menuName = "URP AR/Restoration Object Catalog")]
    public sealed class RestorationObjectCatalog : ScriptableObject
    {
        public RestorationObjectProfile[] objects = Array.Empty<RestorationObjectProfile>();
    }
}
