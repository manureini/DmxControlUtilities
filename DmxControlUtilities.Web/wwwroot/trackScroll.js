// Keeps the horizontal scroll position of all registered track containers in sync.
// Scrolling one container scrolls all others in the same group to the same offset.
(function () {
    var groups = {};

    window.registerTrackScroll = function (groupId, element) {
        if (!element)
            return;

        if (!groups[groupId])
            groups[groupId] = [];

        // avoid double-registration
        if (groups[groupId].indexOf(element) !== -1)
            return;

        var handler = function () {
            if (element._trackScrollSync)
                return;

            var siblings = groups[groupId] || [];

            for (var i = 0; i < siblings.length; i++) {
                var other = siblings[i];

                if (other === element)
                    continue;

                other._trackScrollSync = true;
                other.scrollLeft = element.scrollLeft;
                other._trackScrollSync = false;
            }
        };

        element._trackScrollHandler = handler;
        element.addEventListener('scroll', handler);
        groups[groupId].push(element);
    };

    window.unregisterTrackScroll = function (groupId, element) {
        var siblings = groups[groupId];

        if (!siblings)
            return;

        var index = siblings.indexOf(element);

        if (index !== -1)
            siblings.splice(index, 1);

        if (element && element._trackScrollHandler) {
            element.removeEventListener('scroll', element._trackScrollHandler);
            element._trackScrollHandler = null;
        }

        if (siblings.length === 0)
            delete groups[groupId];
    };

    // Scrolls a container horizontally by the given delta. Used for shift/ctrl + wheel,
    // where the default zoom/scroll is suppressed and we scroll the track instead.
    window.scrollTrackBy = function (element, delta) {
        if (element)
            element.scrollLeft += delta;
    };
})();
