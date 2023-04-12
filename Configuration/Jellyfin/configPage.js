    'use strict';

    function onViewShow() {

        LibraryMenu.setTabs('subbuzz', 0, getTabs);

        $("#SubtitleInfoWithHtmlDescription", this).html('To use this with Jellyfin web interface you need to patch Jellyfin Web Client.')

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

