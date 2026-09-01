// Закрытие выпадающих списков множественного выбора по клику вне их и по Escape.
//
// Родной <details> закрывается только нажатием на сам заголовок: щелчок мимо
// его не трогает, и меню остаётся висеть поверх страницы. От выпадающего
// списка люди ждут обратного, поэтому дослушиваем документ.
//
// Обработчик один и висит на документе, а не на элементах: Blazor перерисовывает
// разметку после каждого нажатия на флажок, и обработчики, навешанные на сами
// элементы, пережили бы не всякую перерисовку.
(function () {
    'use strict';

    const SELECTOR = 'details.kt-multi[open]';

    function closeAllExcept(keep) {
        document.querySelectorAll(SELECTOR).forEach(function (d) {
            if (d !== keep) d.removeAttribute('open');
        });
    }

    // Фаза всплытия: пусть сначала отработают нажатия внутри меню.
    document.addEventListener('click', function (e) {
        const inside = e.target instanceof Element
            ? e.target.closest('details.kt-multi')
            : null;
        closeAllExcept(inside);
    });

    document.addEventListener('keydown', function (e) {
        if (e.key !== 'Escape') return;

        const open = document.querySelector(SELECTOR);
        if (!open) return;

        closeAllExcept(null);
        // Возвращаем фокус на поле, иначе он остаётся на исчезнувшем элементе.
        const summary = open.querySelector('summary');
        if (summary) summary.focus();
    });
})();
