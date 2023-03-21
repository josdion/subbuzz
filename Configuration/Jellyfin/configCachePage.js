    'use strict';

    function onViewShow() {

        LibraryMenu.setTabs('subbuzz', 1, getTabs);
        var page = this;

        $('#SelectCachePath', page).html('<span class="material-icons search" aria-hidden="true"></span>');

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

        view.querySelector('#SelectCachePath').addEventListener('click', function () {
            const picker = new Dashboard.DirectoryBrowser();
            picker.show({
                callback: function (path) {
                    if (path) {
                        view.querySelector('#CachePath').value = path;
                    }

                    picker.close();
                },
                path: view.querySelector('#CachePath').value,
                header: 'Select Cache Path',
                instruction: '',
                validateWriteable: true,
            });
        });

    }

