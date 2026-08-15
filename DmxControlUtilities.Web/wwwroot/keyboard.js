window.registerSpaceToggle = function (dotNetHelper) {
    if (window._spaceToggleHandler) {
        document.removeEventListener('keydown', window._spaceToggleHandler);
        window._spaceToggleHandler = null;
    }

    window._spaceToggleHandler = function (e) {
        if (e.repeat)
            return;

        var tag = (e.target && e.target.tagName) || '';
        if (tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT' || tag === 'BUTTON')
            return;

        if (e.code === 'Space')
            e.preventDefault();

        dotNetHelper.invokeMethodAsync('OnKeyPressed', e.code, e.ctrlKey, e.shiftKey, e.altKey);
    };

    document.addEventListener('keydown', window._spaceToggleHandler);
};

window.unregisterSpaceToggle = function () {
    if (window._spaceToggleHandler) {
        document.removeEventListener('keydown', window._spaceToggleHandler);
        window._spaceToggleHandler = null;
    }
};
