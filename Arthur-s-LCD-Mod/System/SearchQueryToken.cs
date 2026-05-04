using System;
using System.Linq;
using Graph.System.Config.Models.Apps;
using VRage.Game;

namespace Graph.System
{
    /// <summary>
    /// Token for Caching Search Query
    /// </summary>
    public struct SearchQueryToken : IEquatable<SearchQueryToken>
    {
        static readonly SearchQueryToken Empty = new SearchQueryToken();

        readonly long[] _storages;
        readonly string[] _groups;
        readonly MyDefinitionId[] _items;
        readonly string[] _categories;

        readonly int _storagesHash;
        readonly int _groupsHash;
        readonly int _itemsHash;
        readonly int _categoriesHash;

        SearchQueryToken(ScreenConfigWithBlocks config)
        {
            _storages = config.SelectedBlocks;
            _groups = config.SelectedGroups;
            var items = config as ScreenConfigWithItems;
            if (items != null)
            {
                _items = items.SelectedItems;
                _categories = items.SelectedCategories;
                _itemsHash = ComputeArrayHash(_items);
                _categoriesHash = ComputeArrayHash(_categories);
            }
            else
            {
                _items = new MyDefinitionId[] { };
                _categories = new string[] { };
                _itemsHash = 0;
                _categoriesHash = 0;
            }
            
            _storagesHash = ComputeArrayHash(_storages);
            _groupsHash = ComputeArrayHash(_groups);

        }

        static int ComputeArrayHash<T>(T[] array)
        {
            if (array == null) return 0;
            unchecked
            {
                int hash = 17;
                foreach (var item in array)
                    hash = hash * 31 + (item?.GetHashCode() ?? 0);
                return hash;
            }
        }

        public bool Equals(SearchQueryToken other)
        {
            if (!FastEquality(other))
                return false;

            return SequenceEqualSafe(_storages, other._storages)
                   && SequenceEqualSafe(_groups, other._groups)
                   && SequenceEqualSafe(_items, other._items)
                   && SequenceEqualSafe(_categories, other._categories);
        }

        bool FastEquality(SearchQueryToken other)
        {
            return _storagesHash == other._storagesHash
                   && _groupsHash == other._groupsHash
                   && _itemsHash == other._itemsHash
                   && _categoriesHash == other._categoriesHash;
        }
    
        static bool SequenceEqualSafe<T>(T[] a, T[] b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null) return false;
            return a.SequenceEqual(b);
        }

        public override bool Equals(object obj) => obj is SearchQueryToken && Equals((SearchQueryToken)obj);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = _storagesHash;
                hash = (hash * 397) ^ _groupsHash;
                hash = (hash * 397) ^ _itemsHash;
                hash = (hash * 397) ^ _categoriesHash;
                return hash;
            }
        }

        public static SearchQueryToken GetToken(ScreenConfigWithBlocks config)
        {
            var inv = config as ScreenConfigWithItems;
            
            if (!config.SelectedBlocks.Any()
                && !config.SelectedGroups.Any()
                && (inv == null || (!inv.SelectedItems.Any() && !inv.SelectedCategories.Any())))
                return Empty;

            return new SearchQueryToken(config);
        }
    }
}