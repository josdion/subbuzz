    'use strict';

    function onViewShow() {

        LibraryMenu.setTabs('subbuzz', 0, getTabs);

    }

    function getTabs() {
        return [
            {
                href: Dashboard.getPluginUrl('SubbuzzConfigPage'),
                name: 'General'
            },
            {
                href: Dashboard.getPluginUrl('SubbuzzConfigCachePage'),
                name: 'Cache'
            }];
    }

    export default function (view, params) {
        view.addEventListener('viewshow', onViewShow);
    }

