using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using EdFi.Ods.Common;

namespace EdFi.Ods.Api.Extensions
{
    public static class CollectionExtensions
    {
        private static readonly ConcurrentDictionary<Type, Type> _itemTypeByUnderlyingListType = new ConcurrentDictionary<Type, Type>();

        public static bool SynchronizeCollectionTo<T>(
            this ICollection<T> sourceList,
            ICollection<T> targetList,
            Action<T> onChildAdded,
            Func<T, bool> includeItem = null)
            where T : ISynchronizable //<T>
        {
            bool isModified = false;

            // Find items to delete
            var itemsToDelete =
                targetList.Where(ti => includeItem == null || includeItem(ti))
                          .Where(
                               ti => sourceList.Where(i => includeItem == null || includeItem(i))
                                               .All(si => !si.Equals(ti)))
                          .ToList();

            foreach (var item in itemsToDelete)
            {
                // This statement causes failure in NHibernate when no version column is present. 
                //  --> (item as IChildEntity).SetParent(null);
                targetList.Remove(item);
                isModified = true;
            }

            // Copy properties on existing items
            var itemsToUpdate =
                (from p in targetList.Where(i => includeItem == null || includeItem(i))
                 from s in sourceList.Where(i => includeItem == null || includeItem(i))
                 where p.Equals(s)
                 select new
                        {
                            Submitted = s, Persisted = p
                        })
               .ToList();

            foreach (var pair in itemsToUpdate)
            {
                isModified |= pair.Submitted.Synchronize(pair.Persisted);
            }

            // Find items to add
            var itemsToAdd =
                sourceList.Where(i => includeItem == null || includeItem(i))
                          .Except(targetList.Where(i => includeItem == null || includeItem(i)))
                          .ToList();

            foreach (var item in itemsToAdd)
            {
                targetList.Add(item);

                onChildAdded?.Invoke(item);

                isModified = true;
            }

            return isModified;
        }

        public static void MapCollectionTo<TSource, TTarget>(
            this ICollection<TSource> sourceList,
            ICollection<TTarget> targetList,
            object parent = null)
            where TSource : IMappable
        {
            if (sourceList == null)
            {
                return;
            }

            if (targetList == null)
            {
                return;
            }

            var targetListType = targetList.GetType();
            var itemType = GetItemType(targetListType);

            foreach (var sourceItem in sourceList.Distinct())
            {
                TTarget targetItem;

                if (parent != null && itemType.GetConstructors()
                                              .Any(
                                                   c => c.GetParameters()
                                                         .Any(
                                                              p => p.ParameterType == parent.GetType()
                                                                   || p.ParameterType == parent.GetType()
                                                                                               .BaseType)))
                {
                    targetItem = (TTarget) Activator.CreateInstance(itemType, parent);
                }
                else
                {
                    targetItem = (TTarget) Activator.CreateInstance(itemType);
                }

                sourceItem.Map(targetItem);
                targetList.Add(targetItem);
            }
        }

        private static Type GetItemType(Type targetListType)
        {
            Type itemType;

            if (!_itemTypeByUnderlyingListType.TryGetValue(targetListType, out itemType))
            {
                var listTypes = targetListType.GetGenericArguments();

                if (listTypes.Length == 0)
                {
                    throw new ArgumentException(
                        string.Format("Target list type of '{0}' does not have any generic arguments.", targetListType.FullName));
                }

                // Assumption: ItemType is last generic argument (most of the time this will be a List<T>, 
                // but it could be a CovariantIListAdapter<TBase, TDerived>.  We want the last generic argument type.
                itemType = listTypes[listTypes.Length - 1];
                _itemTypeByUnderlyingListType[targetListType] = itemType;
            }

            return itemType;
        }
    }
}