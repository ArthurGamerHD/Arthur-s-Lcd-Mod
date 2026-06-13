namespace LcdMod.Client.Gui.Styling
{
    public static class ScopedResourceResolver
    {
        public static bool TryResolve<TValue>(
            IVisualStyleScope start,
            ResourceKey<TValue> key,
            out TValue value)
        {
            int guard = 0;
            for (IVisualStyleScope scope = start; scope != null && guard++ < 128;)
            {
                if (scope.Resources != null && scope.Resources.TryGet(key, out value))
                    return true;

                IVisualStyleScope next = scope.StyleParent;
                if (ReferenceEquals(next, scope))
                    break;

                scope = next;
            }

            value = default(TValue);
            return false;
        }
    }
}
