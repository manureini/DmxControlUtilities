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

    // Reports the visible window (scroll fraction + visible fraction) of the given group's
    // track containers to .NET whenever it changes (scroll, zoom/resize, content change).
    window.registerViewportNotify = function (groupId, dotNetHelper) {
        var siblings = groups[groupId] || [];

        if (siblings.length === 0)
            return false;

        var element = siblings[0];
        var state = {
            dotNet: dotNetHelper,
            element: element,
            lastLeft: -1,
            lastWidth: -1,
            destroyed: false
        };

        function report() {
            if (state.destroyed || !state.element.isConnected)
                return;

            var el = state.element;
            var max = el.scrollWidth - el.clientWidth;
            var left = max > 0 ? el.scrollLeft / max : 0;
            var width = el.scrollWidth > 0 ? el.clientWidth / el.scrollWidth : 1;

            if (Math.abs(left - state.lastLeft) < 0.0005 && Math.abs(width - state.lastWidth) < 0.0005)
                return;

            state.lastLeft = left;
            state.lastWidth = width;

            state.dotNet.invokeMethodAsync('OnViewportChangedJs', left, width);
        }

        function schedule() {
            if (!state.destroyed)
                requestAnimationFrame(report);
        }

        state.onScroll = schedule;
        element.addEventListener('scroll', state.onScroll);

        state.resizeObserver = new ResizeObserver(schedule);
        state.resizeObserver.observe(element);
        // observe a content child so zoom (which grows the content, not the container) is reported
        if (element.firstElementChild)
            state.resizeObserver.observe(element.firstElementChild);

        window._viewportNotify = state;

        // initial report
        schedule();
        return true;
    };

    window.unregisterViewportNotify = function () {
        var state = window._viewportNotify;

        if (!state)
            return;

        state.destroyed = true;

        if (state.element && state.onScroll)
            state.element.removeEventListener('scroll', state.onScroll);

        if (state.resizeObserver)
            state.resizeObserver.disconnect();

        window._viewportNotify = null;
    };

    // Sets the horizontal scroll of all tracks in a group from a 0..1 fraction of the
    // scrollable range. Used by the overview minimap to drag/click the viewport.
    window.setTrackScrollFraction = function (groupId, fraction) {
        var siblings = groups[groupId] || [];

        for (var i = 0; i < siblings.length; i++) {
            var el = siblings[i];
            var max = el.scrollWidth - el.clientWidth;
            el.scrollLeft = (max > 0 ? max : 0) * fraction;
        }
    };

    // Returns the bounding client rect of an element (used by the overview minimap to
    // translate mouse coordinates into a 0..1 fraction of the strip).
    window.getElementRect = function (element) {
        if (!element)
            return null;

        var r = element.getBoundingClientRect();
        return { left: r.left, top: r.top, width: r.width, height: r.height };
    };
})();
