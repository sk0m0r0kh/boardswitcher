NAME    = boardswitcher
VERSION = 1.0
FILES   = boardswitcher boardswitcher.desktop boardswitcher-menu.desktop \
          boardswitcher.spec LICENSE README.md Makefile .gitignore

.PHONY: dist rpm clean

# Архив с исходниками: dist/boardswitcher-<версия>.tar.gz
dist:
	mkdir -p dist
	tar czf dist/$(NAME)-$(VERSION).tar.gz --transform 's,^,$(NAME)-$(VERSION)/,' $(FILES)

# Сборка rpm и src.rpm в dist/rpmbuild/ (нужен пакет rpm-build)
rpm: dist
	mkdir -p dist/rpmbuild/SOURCES dist/rpmbuild/SPECS dist/rpmbuild/BUILD \
	         dist/rpmbuild/RPMS dist/rpmbuild/SRPMS dist/rpmbuild/BUILDROOT
	cp dist/$(NAME)-$(VERSION).tar.gz dist/rpmbuild/SOURCES/
	cp $(NAME).spec dist/rpmbuild/SPECS/
	rpmbuild --define "_topdir $(CURDIR)/dist/rpmbuild" -ba dist/rpmbuild/SPECS/$(NAME).spec
	@find dist/rpmbuild/RPMS dist/rpmbuild/SRPMS -name '*.rpm'

clean:
	rm -rf dist __pycache__
