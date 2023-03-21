define(['jQuery', 'loading', 'mainTabsManager', 'globalize'], function ($, loading, mainTabsManager, globalize) {
    'use strict';

    function onViewShow() {

        mainTabsManager.setTabs(this, 1, getTabs);
        var page = this;

        $('#SelectCachePath', page).html('<i class="md-icon">search</i>');

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

        view.querySelector('#SelectCachePath').addEventListener('click', function () {
            require(['directorybrowser'], function (directoryBrowser) {
                var picker = new directoryBrowser();
                picker.show({
                    callback: function (path) {
                        if (path) {
                            view.querySelector('#CachePath').value = path;
                        }
                        picker.close();
                    },
                    header: 'Select Cache Path',
                    instruction: '',
                    validateWriteable: true,
                });
            });
        });


    };

});
