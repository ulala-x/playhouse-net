plugins {
    java
    `java-library`
}

group = "com.playhouse"
version = "0.1.0"

java {
    sourceCompatibility = JavaVersion.VERSION_22
    targetCompatibility = JavaVersion.VERSION_22
}

repositories {
    mavenCentral()
}

dependencies {
    // Reference to the main connector module (which already has protobuf)
    api(project(":"))

    // Protobuf is already included in the main connector module
    // But we explicitly declare it here for clarity
    implementation("com.google.protobuf:protobuf-java:3.25.1")

    // Testing
    testImplementation("org.junit.jupiter:junit-jupiter:5.10.0")
    testImplementation("org.assertj:assertj-core:3.24.2")
}

tasks.test {
    useJUnitPlatform()
}

tasks.withType<JavaCompile> {
    options.encoding = "UTF-8"
}
