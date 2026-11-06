
-keep class com.follow.fclash.models.**{ *; }

-keep class com.follow.clash.service.models.**{ *; }

-keep class com.follow.fclash.domain.** { *; }
# User scripts always use Rhino's interpreter (optimizationLevel = -1), safe
# globals and a closed Java class shutter. JVM bytecode generation/Dynalink and
# JavaBean conversion are not exposed on Android. YAML only handles maps/lists
# and scalars, never JavaBeans. Verify these paths in the minified device test.
-keep class !org.mozilla.javascript.optimizer.**,org.mozilla.javascript.** { *; }
-dontwarn jdk.dynalink.**
-dontwarn java.beans.BeanDescriptor
-dontwarn java.beans.BeanInfo
-dontwarn java.beans.FeatureDescriptor
-dontwarn java.beans.IntrospectionException
-dontwarn java.beans.Introspector
-dontwarn java.beans.PropertyDescriptor
