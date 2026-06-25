using LcdMod.Common.Config.Components;
using System;
using System.Linq;

using VRage.Game;
using GridLinkTypeEnum = VRage.Game.ModAPI.GridLinkTypeEnum;
namespace LcdMod.Client.GridData
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
        readonly GridLinkTypeEnum _linkType;

        readonly int _storagesHash;
        readonly int _groupsHash;
        readonly int _itemsHash;
        readonly int _categoriesHash;

        SearchQueryToken(BlockSelectionConfigComponent blocks, ItemSelectionConfigComponent items)
        {
            _storages = blocks?.SelectedBlocks ?? Array.Empty<long>();
            _groups = blocks?.SelectedGroups ?? Array.Empty<string>();
            _linkType = blocks == null ? GridLinkTypeEnum.Mechanical : (GridLinkTypeEnum)blocks.GridLinkTypeInternal;
            _items = items.GetSelectedItems();
            _categories = items?.SelectedCategories ?? Array.Empty<string>();
            _itemsHash = ComputeArrayHash(_items);
            _categoriesHash = ComputeArrayHash(_categories);
            
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
            return _linkType == other._linkType
                   && _storagesHash == other._storagesHash
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
                int hash = (int)_linkType;
                hash = (hash * 397) ^ _storagesHash;
                hash = (hash * 397) ^ _groupsHash;
                hash = (hash * 397) ^ _itemsHash;
                hash = (hash * 397) ^ _categoriesHash;
                return hash;
            }
        }

        public static SearchQueryToken GetToken(BlockSelectionConfigComponent blocks, ItemSelectionConfigComponent items)
        {
            var selectedBlocks = blocks?.SelectedBlocks ?? Array.Empty<long>();
            var selectedGroups = blocks?.SelectedGroups ?? Array.Empty<string>();
            var selectedItems = items.GetSelectedItems();
            var selectedCategories = items?.SelectedCategories ?? Array.Empty<string>();
            var linkType = blocks == null ? GridLinkTypeEnum.Mechanical : (GridLinkTypeEnum)blocks.GridLinkTypeInternal;
            if (linkType == GridLinkTypeEnum.Logical
                && !selectedBlocks.Any()
                && !selectedGroups.Any()
                && !selectedItems.Any()
                && !selectedCategories.Any())
                return Empty;

            return new SearchQueryToken(blocks, items);
        }
    }
}
