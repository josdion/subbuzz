define(['jQuery', 'loading', 'mainTabsManager', 'globalize'], function ($, loading, mainTabsManager, globalize) {
    'use strict';

    function onViewShow() {

        mainTabsManager.setTabs(this, 0, getTabs);
    }

    function getTabs() {
        return [
            {
                href: Dashboard.getConfigurationPageUrl('SubbuzzConfigPage'),
                name: 'General'
            },
            {
                href: Dashboard.getConfigurationPageUrl('SubbuzzConfigCachePage'),
                name: 'Cache'
            }];
    }

    return function (view, params) {
        view.addEventListener('viewshow', onViewShow);
    };

});
